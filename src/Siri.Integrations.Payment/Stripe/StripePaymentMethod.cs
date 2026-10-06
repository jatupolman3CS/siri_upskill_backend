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

        if (string.IsNullOrWhiteSpace(request.CustomerEmail))
        {
            return Result.Failure<PaymentIntentResult>(DomainError.Validation("A customer email is required to process payment."));
        }

        // Amount in satang (smallest currency unit for THB: 1 THB = 100 satang)
        var amountInSatang = (long)Math.Round(request.Amount * 100m, MidpointRounding.AwayFromZero);

        var options = request.Method == PaymentMethodType.Card
            ? new PaymentIntentCreateOptions
              {
                  Amount = amountInSatang,
                  Currency = request.Currency.ToLowerInvariant(),
                  PaymentMethodTypes = ["card"],
                  Confirm = false, // client (Stripe.js Payment Element) confirms
                  // No PaymentMethodData here — client supplies payment method details via confirmPayment()
                  Description = request.Description ?? $"Order {request.OrderNo}",
                  ReceiptEmail = request.CustomerEmail,
                  Metadata = new Dictionary<string, string>
                  {
                      { "orderId", request.OrderId.ToString() },
                      { "orderNo", request.OrderNo }
                  }
              }
            : new PaymentIntentCreateOptions
              {
                  // PromptPay path — unchanged from before P11-09 (byte-for-byte), do not touch this branch
                  Amount = amountInSatang,
                  Currency = request.Currency.ToLowerInvariant(),
                  PaymentMethodTypes = ["promptpay"],
                  Confirm = true,
                  PaymentMethodData = new PaymentIntentPaymentMethodDataOptions
                  {
                      Type = "promptpay",
                      BillingDetails = new PaymentIntentPaymentMethodDataBillingDetailsOptions
                      {
                          Email = request.CustomerEmail,
                      },
                  },
                  Description = request.Description ?? $"Order {request.OrderNo}",
                  ReceiptEmail = request.CustomerEmail,
                  Metadata = new Dictionary<string, string>
                  {
                      { "orderId", request.OrderId.ToString() },
                      { "orderNo", request.OrderNo }
                  }
              };

        var service = new PaymentIntentService(_stripeClient);
        // Reuse the same key across transport retries so an interrupted response cannot create
        // multiple chargeable QR codes for this payment attempt.
        var requestOptions = new RequestOptions { IdempotencyKey = Guid.NewGuid().ToString("N") };

        try
        {
            var intent = await ExecuteWithRetryAsync(
                () => service.CreateAsync(options, requestOptions, cancellationToken),
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

    /// <inheritdoc/>
    public async Task<Result<PaymentRefundResult>> CreateRefundAsync(
        CreateRefundRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(request.ProviderPaymentIntentId);

        if (request.Amount <= 0)
        {
            return Result.Failure<PaymentRefundResult>(DomainError.Validation("Refund amount must be greater than zero."));
        }

        var amountInSatang = (long)Math.Round(request.Amount * 100m, MidpointRounding.AwayFromZero);

        var options = new global::Stripe.RefundCreateOptions
        {
            PaymentIntent = request.ProviderPaymentIntentId,
            Amount = amountInSatang,
            Reason = request.Reason switch
            {
                "duplicate" => "duplicate",
                "fraudulent" => "fraudulent",
                _ => "requested_by_customer"
            }
        };

        var service = new global::Stripe.RefundService(_stripeClient);

        try
        {
            var refund = await ExecuteWithRetryAsync<global::Stripe.Refund>(
                () => service.CreateAsync(options, cancellationToken: cancellationToken),
                "CreateRefund",
                cancellationToken).ConfigureAwait(false);

            var decimalAmount = refund.Amount / 100m;
            return Result.Success(new PaymentRefundResult(refund.Id, refund.Status, decimalAmount, refund.Currency));
        }
        catch (StripeException ex)
        {
            _logger.LogError(ex, "Stripe API error creating refund for PaymentIntent {PaymentIntentId}: {Message}", request.ProviderPaymentIntentId, ex.Message);
            return Result.Failure<PaymentRefundResult>(new DomainError("payment.provider_error", ex.StripeError?.Message ?? ex.Message));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogError(ex, "Unexpected error creating refund for PaymentIntent {PaymentIntentId}", request.ProviderPaymentIntentId);
            return Result.Failure<PaymentRefundResult>(new DomainError("payment.unexpected_error", "An unexpected error occurred while processing refund."));
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// Deliberately a single Stripe API call with no retry, unlike the other methods on this class:
    /// this runs inside the same DB transaction as webhook fulfillment
    /// (<see cref="Siri.Modules.Commerce.Application.StripeWebhookHandler"/>), and retrying (up to
    /// 3 attempts / ~3.5s worst case via <see cref="ExecuteWithRetryAsync{T}"/>) would hold that
    /// transaction open far longer than acceptable for a money/entitlement path. A failed lookup falls
    /// back to <c>Payout:EstimatedPaymentFeePercent</c> (see docs/DECISIONS.md Q4), so a single attempt
    /// with a null fallback is safe. Also deliberately not sourced from the webhook event payload itself:
    /// Stripe does not expand nested objects like balance_transaction in event payloads unless "webhook
    /// endpoint snapshot expansions" are configured out-of-band in the Stripe Dashboard/API, which is not
    /// set up anywhere in this codebase — a follow-up GET via the same IStripeClient avoids that hidden
    /// external dependency.
    /// </remarks>
    public async Task<Result<decimal?>> GetChargeFeeAsync(string providerPaymentIntentId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerPaymentIntentId);
        var service = new PaymentIntentService(_stripeClient);
        try
        {
            var intent = await service.GetAsync(
                providerPaymentIntentId,
                new PaymentIntentGetOptions { Expand = ["latest_charge.balance_transaction"] },
                cancellationToken: cancellationToken).ConfigureAwait(false);

            var feeInSatang = intent.LatestCharge?.BalanceTransaction?.Fee;
            if (feeInSatang is null)
            {
                _logger.LogWarning("PaymentIntent {Id}: balance_transaction.fee not yet available; falling back to estimated fee.", providerPaymentIntentId);
                return Result.Success<decimal?>(null);
            }
            return Result.Success<decimal?>(feeInSatang.Value / 100m);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "GetChargeFeeAsync failed for PaymentIntent {Id}; falling back to estimated fee.", providerPaymentIntentId);
            return Result.Success<decimal?>(null);
        }
    }

    private static PaymentIntentResult MapPaymentIntent(PaymentIntent intent)
    {
        var decimalAmount = intent.Amount / 100m;
        string? qrCodeUrl = null;
        string? qrCodeData = null;

        if (intent.NextAction?.PromptpayDisplayQrCode != null)
        {
            qrCodeUrl = intent.NextAction.PromptpayDisplayQrCode.ImageUrlPng
                ?? intent.NextAction.PromptpayDisplayQrCode.ImageUrlSvg;
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
