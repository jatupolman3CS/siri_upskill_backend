using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Siri.Integrations.Email;

public static class EmailServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="IEmailSender"/> against either a real SMTP server
    /// (<see cref="SmtpEmailSender"/>) or a no-op logger (<see cref="LoggingEmailSender"/>), chosen
    /// by the <c>Email:Provider</c> setting ("Smtp" | "Log", case-insensitive). Falls back to
    /// <see cref="LoggingEmailSender"/> whenever <c>Email:Provider</c> is anything other than "Smtp"
    /// <em>or</em> "Smtp" is selected but <c>Email:Smtp:Host</c> is not actually configured — so a
    /// misconfigured or default (no-credentials-yet) environment still boots and is testable, per
    /// task P0-19, instead of failing startup or silently trying to talk to nothing.
    /// </summary>
    public static IServiceCollection AddEmailIntegration(this IServiceCollection services, IConfiguration configuration)
    {
        var provider = configuration["Email:Provider"];
        var smtpHost = configuration[$"{SmtpEmailSenderOptions.SectionName}:Host"];
        var useSmtp = string.Equals(provider, "Smtp", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(smtpHost);

        if (useSmtp)
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
        else
        {
            services.AddSingleton<IEmailSender, LoggingEmailSender>();
        }

        return services;
    }
}
