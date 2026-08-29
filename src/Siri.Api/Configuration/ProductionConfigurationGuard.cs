using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Siri.Integrations.Payment.Stripe;
using Siri.Modules.Payout;
using Siri.Modules.Payout.Infrastructure;

namespace Siri.Api.Configuration;

/// <summary>
/// Startup guard ensuring production secrets and configurations are strictly validated (P7-12 / docs/TASKS.md).
/// Fails fast on application boot if any insecure default or dev placeholder is detected in Production mode.
/// </summary>
public static class ProductionConfigurationGuard
{
    private const string DevKeyPlaceholder = "CHANGE_ME_IN_PRODUCTION_BASE64_32_BYTES_KEY==";

    public static void ValidateProductionConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsProduction())
        {
            return;
        }

        var errors = new List<string>();

        // 1. DataProtection:EncryptionKeyBase64
        var encKey = configuration["DataProtection:EncryptionKeyBase64"];
        if (string.IsNullOrWhiteSpace(encKey) || string.Equals(encKey, DevKeyPlaceholder, StringComparison.Ordinal))
        {
            errors.Add("DataProtection:EncryptionKeyBase64 must be set to a secure, real key in production (not default placeholder).");
        }
        else
        {
            try
            {
                var bytes = Convert.FromBase64String(encKey);
                if (bytes.Length != 32)
                {
                    errors.Add($"DataProtection:EncryptionKeyBase64 must decode to exactly 32 bytes (256-bit key). Got {bytes.Length} bytes.");
                }
            }
            catch (FormatException)
            {
                errors.Add("DataProtection:EncryptionKeyBase64 is not a valid Base64 string.");
            }
        }

        // 2. Identity:Jwt:SigningKey
        var jwtKey = configuration["Identity:Jwt:SigningKey"];
        if (string.IsNullOrWhiteSpace(jwtKey) || jwtKey.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase) || jwtKey.Length < 32)
        {
            errors.Add("Identity:Jwt:SigningKey must be a secure production key at least 32 characters long.");
        }

        // 3. Stripe Keys
        var stripeSecretKey = configuration[$"{StripeOptions.SectionName}:SecretKey"];
        if (string.IsNullOrWhiteSpace(stripeSecretKey) || !stripeSecretKey.StartsWith("sk_live_", StringComparison.Ordinal))
        {
            errors.Add($"{StripeOptions.SectionName}:SecretKey must be a live secret key starting with 'sk_live_' in production.");
        }

        var stripeWebhookSecret = configuration[$"{StripeOptions.SectionName}:WebhookSecret"];
        if (!string.IsNullOrWhiteSpace(stripeWebhookSecret) && !stripeWebhookSecret.StartsWith("whsec_", StringComparison.Ordinal))
        {
            errors.Add($"{StripeOptions.SectionName}:WebhookSecret must be a valid webhook secret starting with 'whsec_' in production.");
        }

        // 4. CORS Allowed Origins
        var corsOrigins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
        if (corsOrigins.Length == 0)
        {
            errors.Add("Cors:AllowedOrigins must be specified with exact production frontend domains in production.");
        }
        else
        {
            foreach (var origin in corsOrigins)
            {
                if (origin == "*" || origin.Contains("localhost", StringComparison.OrdinalIgnoreCase))
                {
                    errors.Add($"Cors:AllowedOrigins cannot contain wildcards or localhost in production. Found: '{origin}'");
                }
            }
        }

        // 5. SEO Public Base URL
        var publicBaseUrl = configuration["Seo:PublicBaseUrl"];
        if (string.IsNullOrWhiteSpace(publicBaseUrl) || !publicBaseUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add("Seo:PublicBaseUrl must be configured with a secure https:// domain in production.");
        }

        // 6. Payout: Payer info for 50 ทวิ Tax Certificates (P6-04 / X-9)
        var payerCompanyName = configuration[$"{PayoutOptions.SectionName}:PayerCompanyName"];
        var payerTaxId = configuration[$"{PayoutOptions.SectionName}:PayerTaxId"];
        var payerAddress = configuration[$"{PayoutOptions.SectionName}:PayerAddress"];
        var payoutOptions = new PayoutOptions
        {
            PayerCompanyName = payerCompanyName ?? string.Empty,
            PayerTaxId = payerTaxId ?? string.Empty,
            PayerAddress = payerAddress ?? string.Empty
        };

        try
        {
            PayoutOptionsGuard.EnsureRealPayerInfoConfigured(payoutOptions);
        }
        catch (InvalidOperationException ex)
        {
            errors.Add(ex.Message);
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "PRODUCTION CONFIGURATION VALIDATION FAILED:\n - " + string.Join("\n - ", errors));
        }
    }
}
