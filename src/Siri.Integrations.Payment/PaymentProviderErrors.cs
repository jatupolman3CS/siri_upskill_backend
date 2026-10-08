using Siri.SharedKernel;

namespace Siri.Integrations.Payment;

/// <summary>
/// Stable <see cref="DomainError"/> codes the payment integration returns for misconfiguration. Any
/// code ending in <see cref="DomainErrorHttpResults.NotConfiguredCodeSuffix"/> is answered by the API
/// as HTTP 503 ProblemDetails.
/// </summary>
public static class PaymentProviderErrors
{
    /// <summary>Stripe keys are missing or still placeholders. There is no fake-key fallback, so no
    /// Stripe call is ever attempted in this state.</summary>
    public const string ProviderNotConfiguredCode = "payment.provider_not_configured";

    /// <summary>Generic text on purpose: this reaches API clients as the ProblemDetails title, so which
    /// setting is missing is only logged server-side (never echoed to callers).</summary>
    public static DomainError ProviderNotConfigured() =>
        new(ProviderNotConfiguredCode, "Payment provider is not configured.");
}
