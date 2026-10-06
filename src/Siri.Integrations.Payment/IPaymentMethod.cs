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

    /// <summary>Creates a refund for a payment on the provider.</summary>
    Task<Result<PaymentRefundResult>> CreateRefundAsync(
        CreateRefundRequest request,
        CancellationToken cancellationToken);

    /// <summary>Best-effort lookup of the actual fee Stripe deducted from this PaymentIntent's charge
    /// (same unit as Amount, i.e. THB not satang). Contract: always returns Result.Success(null) when
    /// the fee cannot be determined (transient API error, fee not yet settled, etc.) — never returns
    /// Failure because callers always have a fallback (Payout:EstimatedPaymentFeePercent config, see
    /// docs/DECISIONS.md Q4) and this call must never fail payment fulfillment.</summary>
    Task<Result<decimal?>> GetChargeFeeAsync(string providerPaymentIntentId, CancellationToken cancellationToken);
}

/// <summary>Provider-level payment method type. Intentionally separate from
/// <see cref="Siri.Modules.Commerce.Domain.PaymentMethod"/> — this project only references
/// <c>Siri.SharedKernel</c>, so referencing Commerce would invert the module dependency direction.
/// <see cref="Siri.Modules.Commerce.Application.PaymentService"/> maps between the two.</summary>
public enum PaymentMethodType { PromptPay, Card }

public sealed record CreatePaymentIntentRequest(
    Guid OrderId,
    string OrderNo,
    decimal Amount,
    string Currency = "thb",
    string? Description = null,
    string? CustomerEmail = null,
    PaymentMethodType Method = PaymentMethodType.PromptPay);

public sealed record PaymentIntentResult(
    string PaymentIntentId,
    string ClientSecret,
    string Status,
    decimal Amount,
    string Currency,
    string? QrCodeUrl,
    string? QrCodeData);

public sealed record CreateRefundRequest(
    string ProviderPaymentIntentId,
    decimal Amount,
    string? Reason = null);

public sealed record PaymentRefundResult(
    string RefundId,
    string Status,
    decimal Amount,
    string Currency);
