using System.ComponentModel.DataAnnotations;

namespace Siri.Integrations.Email;

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> ("Email:Smtp"). Deliberately just
/// standard SMTP settings — no vendor-specific fields (no SendGrid/SES API keys, etc.): there is no
/// production email vendor decision yet (unlike Bunny Stream/Stripe/Contabo, already decided per
/// CLAUDE.md), so this stays vendor-neutral and works with any provider that exposes an SMTP
/// endpoint by only changing configuration later.
/// <para>
/// appsettings*.json only ever carries non-secret placeholder values for these — real host/
/// credentials belong in <c>dotnet user-secrets</c> (dev) or environment variables/Key Vault (prod),
/// per CLAUDE.md rule 9 and .claude/rules/security.md's "Secrets" section.
/// </para>
/// </summary>
public sealed class SmtpEmailSenderOptions
{
    public const string SectionName = "Email:Smtp";

    [Required]
    public string Host { get; set; } = string.Empty;

    public int Port { get; set; } = 587;

    /// <summary><c>null</c>/empty means connect without authentication (some internal relays allow
    /// this) — <see cref="SmtpEmailSender"/> only authenticates when a username is present.</summary>
    public string? Username { get; set; }

    public string? Password { get; set; }

    [Required]
    public string FromAddress { get; set; } = string.Empty;

    public string FromDisplayName { get; set; } = "SIRI UpSkill";

    /// <summary>
    /// <c>true</c> (default): connect in plaintext then upgrade via STARTTLS — the typical choice for
    /// port 587. <c>false</c>: connect already TLS-encrypted (implicit TLS) — the typical choice for
    /// port 465. See <see cref="SmtpEmailSender"/> for how this maps to MailKit's
    /// <c>SecureSocketOptions</c>.
    /// </summary>
    public bool UseStartTls { get; set; } = true;
}
