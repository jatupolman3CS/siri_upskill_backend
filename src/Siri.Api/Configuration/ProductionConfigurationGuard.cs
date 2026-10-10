using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Npgsql;
using Siri.Integrations.Email;
using Siri.Integrations.Payment.Stripe;
using Siri.Integrations.Video.Bunny;
using Siri.Modules.Catalog;
using Siri.Modules.Commerce;
using Siri.Modules.Learning;
using Siri.Modules.Live;
using Siri.Modules.Notification.Infrastructure.Delivery;
using Siri.Modules.Payout;
using Siri.Modules.Payout.Infrastructure;
using Siri.SharedKernel.Configuration;
using Siri.Workers;

namespace Siri.Api.Configuration;

/// <summary>
/// Startup guard ensuring production secrets and configurations are strictly validated (P7-12 / docs/TASKS.md).
/// Fails fast on application boot if any insecure default or dev placeholder is detected in Production mode.
/// </summary>
public static class ProductionConfigurationGuard
{
    public static void ValidateProductionConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        if (!environment.IsProduction())
        {
            return;
        }

        var errors = new List<string>();

        // 1. DataProtection:EncryptionKeyBase64 — shared with Siri.Workers (which decrypts stored Google refresh tokens), so the two
        // hosts can never disagree about what a production-grade key is.
        errors.AddRange(DataProtectionProductionRequirements.GetProblems(configuration));

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

        // A missing webhook secret means paid orders are never fulfilled (every Stripe event is
        // rejected), and a placeholder one would be a publicly-known signing secret — so it is required.
        var stripeWebhookSecret = configuration[$"{StripeOptions.SectionName}:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(stripeWebhookSecret) || !stripeWebhookSecret.StartsWith("whsec_", StringComparison.Ordinal))
        {
            errors.Add($"{StripeOptions.SectionName}:WebhookSecret must be set to the real webhook signing secret starting with 'whsec_' in production.");
        }

        var stripePublishableKey = configuration[$"{StripeOptions.SectionName}:PublishableKey"];
        if (string.IsNullOrWhiteSpace(stripePublishableKey) || !stripePublishableKey.StartsWith("pk_live_", StringComparison.Ordinal))
        {
            errors.Add($"{StripeOptions.SectionName}:PublishableKey must be a live publishable key starting with 'pk_live_' in production.");
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

        // 7. Email: real SMTP delivery only. 'Log'/unset silently drop (or fail) every mail — password
        // resets, receipts and confirmations — which must never go unnoticed in production.
        errors.AddRange(EmailProductionRequirements.GetProblems(configuration));

        // 8. Video: every Bunny Stream setting must be real. There is no mock fallback, so a missing
        // value would only surface later as 503s on upload/playback — fail the boot instead.
        var videoOptions = configuration.GetSection(VideoProviderOptions.SectionName).Get<VideoProviderOptions>()
            ?? new VideoProviderOptions();
        var missingVideoSettings = videoOptions.GetAllMissingSettings();
        if (missingVideoSettings.Count > 0)
        {
            errors.Add(
                "Bunny Stream settings must be set to real values in production (empty or placeholder): "
                + string.Join(", ", missingVideoSettings) + ".");
        }

        // 9. Receipts / tax invoices: the SELLER printed on every document must be the real legal entity.
        // There is no built-in company, so a missing/placeholder value would only surface later as 503s on
        // the receipt endpoints — fail the boot instead.
        // Effective identity: Commerce:Seller:* with the payout module's payer identity as the fallback.
        var sellerOptions = ReceiptSellerOptions.Resolve(configuration);
        var missingSellerSettings = sellerOptions.GetMissingSettings();
        if (missingSellerSettings.Count > 0)
        {
            errors.Add(
                "Receipt seller identity must be set to the real legal entity in production (missing, placeholder or invalid): "
                + string.Join(", ", missingSellerSettings) + ".");
        }

        // 10. Certificates: the QR code printed on every certificate PDF must lead to the real public site.
        // The effective value is the explicit Learning:Certificates:PublicBaseUrl, else the site-wide Seo:PublicBaseUrl.
        var certificateOptions = new CertificateOptions
        {
            PublicBaseUrl = CertificateOptions.ResolvePublicBaseUrl(
                configuration[$"{CertificateOptions.SectionName}:PublicBaseUrl"], configuration),
        };
        if (certificateOptions.GetNormalizedPublicBaseUrl() is not { } normalizedCertificateUrl
            || !normalizedCertificateUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            errors.Add(
                $"{CertificateOptions.SectionName}:PublicBaseUrl (or {CertificateOptions.FallbackConfigurationKey}) " +
                "must be configured with the real public https:// origin in production.");
        }

        // 11. Attachments: no virus-scanning engine is integrated (Q9). Until 2026-10-10 this rule refused to boot with
        // Attachments:VirusScan:Mode=Disabled; the project owner then explicitly chose to skip the scan step for now
        // (docs/DECISIONS.md Q9) so teaching documents can be uploaded in production. Disabled is therefore allowed — it
        // stays an explicit, logged opt-in (every unscanned file logs a warning), and the other upload defences are unchanged:
        // extension + MIME allow-list, magic bytes, executable-header rejection, owner/admin-only uploads, private R2 bucket,
        // Content-Disposition: attachment, short-lived signed URLs. Once a real scanning engine is registered this mode no longer applies.

        // 12. Live (P11-03): the fake Google/Meet provider must never run in production, a half-configured Google
        // client would only fail at an instructor's first click, and the public origin goes into links people follow.
        // Shared with Siri.Workers (which runs the sync job) so the two hosts cannot drift apart.
        errors.AddRange(LiveProductionRequirements.GetProblems(configuration));

        // 13. Notification pipeline on Kafka: only a host that runs the pipeline needs the broker settings, and the API runs it exactly
        // when it hosts the Hangfire server itself (a dedicated Siri.Workers deployment checks its own copy of this rule).
        if (HangfireHostingOptions.ResolveServerInApi(configuration, environment))
        {
            errors.AddRange(NotificationDeliveryProductionRequirements.GetProblems(configuration));
        }

        if (errors.Count > 0)
        {
            throw new InvalidOperationException(
                "PRODUCTION CONFIGURATION VALIDATION FAILED:\n - " + string.Join("\n - ", errors));
        }
    }

    /// <summary>
    /// True when the process is running inside the app's own Docker image (set automatically by the
    /// official aspnet base image regardless of ASPNETCORE_ENVIRONMENT) — the one reliable signal that
    /// this is a real deployment (QA/Production) rather than a developer's native Windows machine,
    /// even if ASPNETCORE_ENVIRONMENT was mistakenly left as "Development" on the deployed container.
    /// </summary>
    public static bool IsRunningInContainer(IConfiguration configuration) =>
        configuration.GetValue("DOTNET_RUNNING_IN_CONTAINER", false);

    public static void ValidateDeploymentConfiguration(IConfiguration configuration, IHostEnvironment environment)
    {
        var runsInContainer = IsRunningInContainer(configuration);
        if (environment.IsDevelopment() && !runsInContainer)
        {
            return;
        }

        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            connectionString = configuration["ConnectionStrings__Default"];
        }

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            return;
        }

        NpgsqlConnectionStringBuilder builder;
        try
        {
            builder = new NpgsqlConnectionStringBuilder(connectionString);
        }
        catch (ArgumentException ex)
        {
            throw new InvalidOperationException("ConnectionStrings:Default must be a valid PostgreSQL connection string.", ex);
        }

        var host = builder.Host;
        var isLoopback = string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
            || string.Equals(host, "::1", StringComparison.OrdinalIgnoreCase);

        var isNativeDevDatabase = isLoopback
            && builder.Port == 5433
            && string.Equals(builder.Username, "siriupskill_dev", StringComparison.OrdinalIgnoreCase);

        if (isNativeDevDatabase)
        {
            throw new InvalidOperationException(
                "Deployment database configuration points at the native Windows development database " +
                "(127.0.0.1:5433 / siriupskill_dev). For QA, set ConnectionStrings__Default in .env " +
                "to the QA PostgreSQL host reachable from the deployed app, for example Host=postgres;Port=5432;Database=SIRIUPSKILL;Username=siriupskill_app;Password=...");
        }
    }
}
