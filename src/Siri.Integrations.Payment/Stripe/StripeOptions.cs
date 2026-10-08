namespace Siri.Integrations.Payment.Stripe;

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> ("Payment:Stripe"). Options pattern +
/// <c>ValidateOnStart()</c> per backend.md's Configuration section.
/// <para>
/// Nothing here is <c>[Required]</c> at startup on purpose: a Development/QA host without Stripe keys
/// must still boot so every non-payment feature works. There is no fake/placeholder key fallback —
/// instead <see cref="StripePaymentMethod"/> refuses every Stripe call and the payment config endpoint
/// answers HTTP 503 <c>payment.provider_not_configured</c> until real keys are set. Production is
/// fail-fast: <c>ProductionConfigurationGuard</c> refuses to start with missing/non-live keys.
/// </para>
/// <para>
/// Real API keys are stored in <c>dotnet user-secrets</c> (dev) / env (prod) per security.md — never committed.
/// </para>
/// </summary>
public sealed class StripeOptions
{
    public const string SectionName = "Payment:Stripe";

    /// <summary>Stripe Secret Key (sk_test_... or sk_live_...).</summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Stripe Publishable Key (pk_test_... or pk_live_...).</summary>
    public string PublishableKey { get; set; } = string.Empty;

    /// <summary>Stripe Webhook Signing Secret (whsec_...).</summary>
    public string WebhookSecret { get; set; } = string.Empty;

    /// <summary>
    /// True when <paramref name="value"/> is a real configured value: not empty/whitespace and not one
    /// of the <c>CHANGE_ME…</c> markers committed in appsettings/.env templates. A committed marker is
    /// publicly known, so e.g. a webhook secret taken from it would let anyone forge Stripe events.
    /// </summary>
    public static bool IsConfigured(string? value) =>
        !string.IsNullOrWhiteSpace(value) && !value.StartsWith("CHANGE_ME", StringComparison.OrdinalIgnoreCase);

    /// <summary>True when Stripe API calls (create/get/cancel PaymentIntent, refunds) can be made.</summary>
    public bool HasSecretKey => IsConfigured(SecretKey);

    /// <summary>True when the client-side publishable key can be handed to the frontend.</summary>
    public bool HasPublishableKey => IsConfigured(PublishableKey);

    /// <summary>True when incoming webhook signatures can be verified.</summary>
    public bool HasWebhookSecret => IsConfigured(WebhookSecret);
}
