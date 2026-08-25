using Siri.SharedKernel;

namespace Siri.Integrations.Payment;

/// <summary>
/// Abstraction over payment gateway providers (Stripe for PromptPay QR in v1, expandable to Card/other gateways).
/// Replaces the obsolete <see cref="IPaymentVerifier"/> (which was for EasySlip).
/// </summary>
public interface IPaymentMethod
{
    /// <summary>Creates a PaymentIntent on the provider for an order.</summary>
    Task<Result<PaymentIntentResult>> CreatePaymentIntentAsync(
        CreatePaymentIntentRequest request,
        CancellationToken cancellationToken);

    /// <summary>Fetches the current status and details of a PaymentIntent by ID.</summary>
    Task<Result<PaymentIntentResult>> GetPaymentIntentAsync(
        string providerPaymentIntentId,
        CancellationToken cancellationToken);

    /// <summary>Cancels an uncaptured/pending PaymentIntent on the provider (e.g. order expiry).</summary>
    Task<Result> CancelPaymentIntentAsync(
        string providerPaymentIntentId,
        CancellationToken cancellationToken);
}

public sealed record CreatePaymentIntentRequest(
    Guid OrderId,
    string OrderNo,
    decimal Amount,
    string Currency = "thb",
    string? Description = null,
    string? CustomerEmail = null);

public sealed record PaymentIntentResult(
    string PaymentIntentId,
    string ClientSecret,
    string Status,
    decimal Amount,
    string Currency,
    string? QrCodeUrl,
    string? QrCodeData);
