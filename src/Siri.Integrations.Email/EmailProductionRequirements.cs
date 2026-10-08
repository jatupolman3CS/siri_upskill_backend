using System.Net.Mail;
using Microsoft.Extensions.Configuration;

namespace Siri.Integrations.Email;

/// <summary>
/// What a Production host must have configured for email to actually be delivered. Shared by every
/// process that sends mail (the API's <c>ProductionConfigurationGuard</c> and the Workers host, which
/// drains the email outbox) so neither can start in Production while silently dropping mail.
/// </summary>
public static class EmailProductionRequirements
{
    private static readonly string[] FakeDomainSuffixes = [".test", ".invalid", ".localhost", ".example"];
    private static readonly string[] FakeDomains = ["example.com", "example.org", "example.net"];

    /// <summary>Human-readable problems (never including secret values); empty when mail delivery is
    /// properly configured.</summary>
    public static IReadOnlyList<string> GetProblems(IConfiguration configuration)
    {
        var problems = new List<string>();

        var provider = configuration["Email:Provider"]?.Trim();
        if (!string.Equals(provider, EmailServiceCollectionExtensions.SmtpProvider, StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(
                $"Email:Provider must be '{EmailServiceCollectionExtensions.SmtpProvider}' in production " +
                $"(got '{(string.IsNullOrEmpty(provider) ? "<unset>" : provider)}'): 'Log' and an unset provider deliver no mail.");
            return problems;
        }

        if (string.IsNullOrWhiteSpace(configuration[$"{SmtpEmailSenderOptions.SectionName}:Host"]))
        {
            problems.Add($"{SmtpEmailSenderOptions.SectionName}:Host must be set to the real SMTP server in production.");
        }

        var from = configuration[$"{SmtpEmailSenderOptions.SectionName}:FromAddress"];
        if (string.IsNullOrWhiteSpace(from))
        {
            problems.Add($"{SmtpEmailSenderOptions.SectionName}:FromAddress must be set to a real sender address in production.");
        }
        else if (!MailAddress.TryCreate(from, out var address) || IsFakeDomain(address.Host))
        {
            problems.Add($"{SmtpEmailSenderOptions.SectionName}:FromAddress must be a real, deliverable sender address in production (not a placeholder/reserved domain).");
        }

        if (bool.TryParse(configuration[$"{SmtpEmailSenderOptions.SectionName}:AllowInsecure"], out var insecure) && insecure)
        {
            problems.Add($"{SmtpEmailSenderOptions.SectionName}:AllowInsecure must be false in production (plaintext SMTP is Development-only).");
        }

        return problems;
    }

    private static bool IsFakeDomain(string host) =>
        FakeDomains.Contains(host, StringComparer.OrdinalIgnoreCase)
        || FakeDomainSuffixes.Any(suffix => host.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
}
