using System.ComponentModel.DataAnnotations;

namespace Siri.Integrations.Payment.Stripe;

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> ("Payment:Stripe"). Options pattern +
/// <c>ValidateOnStart()</c> per backend.md's Configuration section.
/// <para>
/// Real API keys are stored in <c>dotnet user-secrets</c> (dev) / env (prod) per security.md — never committed.
/// </para>
/// </summary>
public sealed class StripeOptions
{
    public const string SectionName = "Payment:Stripe";

    /// <summary>Stripe Secret Key (sk_test_... or sk_live_...).</summary>
    [Required]
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>Stripe Publishable Key (pk_test_... or pk_live_...).</summary>
    [Required]
    public string PublishableKey { get; set; } = string.Empty;

    /// <summary>Stripe Webhook Signing Secret (whsec_...).</summary>
    public string WebhookSecret { get; set; } = string.Empty;
}
