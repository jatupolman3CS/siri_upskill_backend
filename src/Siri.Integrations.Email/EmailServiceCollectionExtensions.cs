using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Siri.Integrations.Email;

public static class EmailServiceCollectionExtensions
{
    /// <summary>The only <c>Email:Provider</c> value that delivers real mail.</summary>
    public const string SmtpProvider = "Smtp";

    /// <summary>Explicit opt-in that deliberately delivers nothing (logs only). Never a default.</summary>
    public const string LogProvider = "Log";

    /// <summary>
    /// Registers <see cref="IEmailSender"/> from the <c>Email:Provider</c> setting (case-insensitive):
    /// <list type="bullet">
    /// <item><c>Smtp</c> — real delivery via <see cref="SmtpEmailSender"/>. <c>Email:Smtp:Host</c> and
    /// <c>FromAddress</c> are required and validated on start, so an incomplete SMTP configuration
    /// fails the boot loudly instead of silently dropping mail.</item>
    /// <item><c>Log</c> — <see cref="LoggingEmailSender"/>; delivers nothing. Only ever selected by
    /// explicitly setting this value (tests, or an operator who really wants no mail).
    /// <c>ProductionConfigurationGuard</c> refuses it in Production.</item>
    /// <item>unset/empty — <see cref="UnconfiguredEmailSender"/>: the host boots (so other features work)
    /// but every send fails with <c>email.provider_not_configured</c>, which the email outbox records and
    /// retries — mail is never silently pretended-sent.</item>
    /// <item>anything else — a typo; throws at registration time.</item>
    /// </list>
    /// </summary>
    public static IServiceCollection AddEmailIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Email:Provider"]?.Trim();

        if (string.Equals(provider, SmtpProvider, StringComparison.OrdinalIgnoreCase))
        {
            services
                .AddOptions<SmtpEmailSenderOptions>()
                .Bind(configuration.GetSection(SmtpEmailSenderOptions.SectionName))
                .ValidateDataAnnotations()
                .Validate<IHostEnvironment>(
                    (options, environment) => !options.AllowInsecure || environment.IsDevelopment(),
                    "Unencrypted SMTP is allowed only in Development.")
                .ValidateOnStart();

            services.AddSingleton<IEmailSender, SmtpEmailSender>();
        }
        else if (string.Equals(provider, LogProvider, StringComparison.OrdinalIgnoreCase))
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }
        else if (string.IsNullOrEmpty(provider))
        {
            services.AddSingleton<IEmailSender, UnconfiguredEmailSender>();
        }
        else
        {
            throw new InvalidOperationException(
                $"Unknown 'Email:Provider' value '{provider}'. Use '{SmtpProvider}' (real delivery) or '{LogProvider}' (explicitly deliver nothing).");
        }

        return services;
    }
}
