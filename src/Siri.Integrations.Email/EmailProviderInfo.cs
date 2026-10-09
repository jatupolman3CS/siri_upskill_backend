namespace Siri.Integrations.Email;

/// <summary>
/// Which e-mail provider this process was configured with (<c>Email:Provider</c>), as a registered fact — so diagnostics can report it without
/// re-reading configuration or sniffing the type of the sender. <see cref="Name"/> is one of <see cref="Smtp"/> (real delivery),
/// <see cref="Log"/> (deliberately delivers nothing) or <see cref="Unconfigured"/> (no provider set: every send fails and is retried later).
/// Carries no setting values — never a host name, user name or password.
/// </summary>
public sealed record EmailProviderInfo(string Name)
{
    public const string Smtp = EmailServiceCollectionExtensions.SmtpProvider;

    public const string Log = EmailServiceCollectionExtensions.LogProvider;

    public const string Unconfigured = "Unconfigured";

    /// <summary>True only for real delivery.</summary>
    public bool DeliversMail => string.Equals(Name, Smtp, StringComparison.Ordinal);
}
