using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.SharedKernel;
using Stripe;

namespace Siri.Integrations.Payment.Stripe;

/// <summary>
/// <see cref="IPaymentMethod"/> implementation backed by Stripe API (PaymentIntent + PromptPay).
/// </summary>
public sealed class StripePaymentMethod : IPaymentMethod
{
    private const int MaxRetries = 3;
    private static readonly TimeSpan[] RetryDelays = [
        TimeSpan.FromMilliseconds(500),
        TimeSpan.FromSeconds(1),
        TimeSpan.FromSeconds(2),
    ];

    private readonly StripeOptions _options;
    private readonly ILogger<StripePaymentMethod> _logger;
    private readonly IStripeClient _stripeClient;

    public StripePaymentMethod(
        IOptions<StripeOptions> options,
        ILogger<StripePaymentMethod> logger,
        IStripeClient? stripeClient = null)
    {
        _options = options.Value;
        _logger = logger;
        _stripeClient = stripeClient ?? new StripeClient(_options.SecretKey);
    }

    /// <inheritdoc/>
    public async Task<Result<PaymentIntentResult>> CreatePaymentIntentAsync(
        CreatePaymentIntentRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (request.Amount <= 0)
        {
            return Result.Failure<PaymentIntentResult>(DomainError.Validation("Payment amount must be greater than zero."));
        }

        // Amount in satang (smallest currency unit for THB: 1 THB = 100 satang)
        var amountInSatang = (long)Math.Round(request.Amount * 100m, MidpointRounding.AwayFromZero);

        var options = new PaymentIntentCreateOptions
        {
            Amount = amountInSatang,
            Currency = request.Currency.ToLowerInvariant(),
            PaymentMethodTypes = ["promptpay"],
            Description = request.Description ?? $"Order {request.OrderNo}",
            ReceiptEmail = request.CustomerEmail,
            Metadata = new Dictionary<string, string>
            {
                { "orderId", request.OrderId.ToString() },
                { "orderNo", request.OrderNo }
            }
        };

        var service = new PaymentIntentService(_stripeClient);

        try
        {
            var intent = await ExecuteWithRetryAsync(
                () => service.CreateAsync(options, cancellationToken: cancellationToken),
                "CreatePaymentIntent",
                cancellationToken).ConfigureAwait(false);

            return Result.Success(MapPaymentIntent(intent));
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe API error while creating PaymentIntent for Order {OrderNo}: {Message}", request.OrderNo, ex.Message);
            return Result.Failure<PaymentIntentResult>(new DomainError("payment.provider_error", ex.StripeError?.Message ?? ex.Message));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected error creating PaymentIntent for Order {OrderNo}", request.OrderNo);
            return Result.Failure<PaymentIntentResult>(new DomainError("payment.unexpected_error", "An unexpected error occurred while processing payment."));
        }
    }

    /// <inheritdoc/>
    public async Task<Result<PaymentIntentResult>> GetPaymentIntentAsync(
        string providerPaymentIntentId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerPaymentIntentId);

        var service = new PaymentIntentService(_stripeClient);

        try
        {
            var intent = await ExecuteWithRetryAsync(
                () => service.GetAsync(providerPaymentIntentId, cancellationToken: cancellationToken),
                "GetPaymentIntent",
                cancellationToken).ConfigureAwait(false);

            return Result.Success(MapPaymentIntent(intent));
        }
        catch (StripeException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Result.Failure<PaymentIntentResult>(DomainError.NotFound($"PaymentIntent '{providerPaymentIntentId}' not found."));
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe API error getting PaymentIntent {PaymentIntentId}: {Message}", providerPaymentIntentId, ex.Message);
            return Result.Failure<PaymentIntentResult>(new DomainError("payment.provider_error", ex.StripeError?.Message ?? ex.Message));
        }
    }

    /// <inheritdoc/>
    public async Task<Result> CancelPaymentIntentAsync(
        string providerPaymentIntentId,
        CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerPaymentIntentId);

        var service = new PaymentIntentService(_stripeClient);

        try
        {
            await ExecuteWithRetryAsync(
                () => service.CancelAsync(providerPaymentIntentId, cancellationToken: cancellationToken),
                "CancelPaymentIntent",
                cancellationToken).ConfigureAwait(false);

            return Result.Success();
        }
        catch (StripeException ex) when (ex.HttpStatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return Result.Success(); // Idempotent: already gone or doesn't exist
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe API error cancelling PaymentIntent {PaymentIntentId}: {Message}", providerPaymentIntentId, ex.Message);
            return Result.Failure(new DomainError("payment.provider_error", ex.StripeError?.Message ?? ex.Message));
        }
    }

    private static PaymentIntentResult MapPaymentIntent(PaymentIntent intent)
    {
        var decimalAmount = intent.Amount / 100m;
        string? qrCodeUrl = null;
        string? qrCodeData = null;

        if (intent.NextAction?.PromptpayDisplayQrCode != null)
        {
            qrCodeUrl = intent.NextAction.PromptpayDisplayQrCode.HostedInstructionsUrl;
            qrCodeData = intent.NextAction.PromptpayDisplayQrCode.Data;
        }

        return new PaymentIntentResult(
            intent.Id,
            intent.ClientSecret,
            intent.Status,
            decimalAmount,
            intent.Currency,
            qrCodeUrl,
            qrCodeData);
    }

    private async Task<T> ExecuteWithRetryAsync<T>(
        Func<Task<T>> action,
        string operationName,
        CancellationToken cancellationToken)
    {
        for (var attempt = 0; attempt <= MaxRetries; attempt++)
        {
            if (attempt > 0)
            {
                var delay = RetryDelays[Math.Min(attempt - 1, RetryDelays.Length - 1)];
                _logger.LogWarning("Stripe API call {Operation} failed (attempt {Attempt}/{MaxRetries}), retrying in {Delay}ms.",
                    operationName, attempt, MaxRetries + 1, delay.TotalMilliseconds);
                await Task.Delay(delay, cancellationToken).ConfigureAwait(false);
            }

            try
            {
                return await action().ConfigureAwait(false);
            }
            catch (StripeException ex) when (attempt < MaxRetries && IsTransient(ex))
            {
                _logger.LogWarning(ex, "Transient StripeException on {Operation} (attempt {Attempt}/{MaxRetries}).",
                    operationName, attempt + 1, MaxRetries + 1);
            }
            catch (HttpRequestException ex) when (attempt < MaxRetries)
            {
                _logger.LogWarning(ex, "HttpRequestException on {Operation} (attempt {Attempt}/{MaxRetries}).",
                    operationName, attempt + 1, MaxRetries + 1);
            }
        }

        return await action().ConfigureAwait(false);
    }

    private static bool IsTransient(StripeException ex) =>
        ex.HttpStatusCode is System.Net.HttpStatusCode.TooManyRequests
            or System.Net.HttpStatusCode.InternalServerError
            or System.Net.HttpStatusCode.BadGateway
            or System.Net.HttpStatusCode.ServiceUnavailable
            or System.Net.HttpStatusCode.GatewayTimeout;
}
