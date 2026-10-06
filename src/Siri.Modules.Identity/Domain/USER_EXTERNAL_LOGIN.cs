using Siri.SharedKernel;

namespace Siri.Modules.Identity.Domain;

/// <summary>Identity providers a <see cref="USER"/> can sign in with besides email + password.</summary>
public enum ExternalLoginProvider
{
    Google = 1,
}

/// <summary>
/// Links a <see cref="USER"/> to an account at an external identity provider (Google today). The
/// provider's stable subject id (<see cref="ProviderSubject"/>, Google's <c>sub</c> claim) — never the
/// email address — is the identity key: an email can change or be recycled at the provider, the
/// subject cannot. (<see cref="Provider"/>, <see cref="ProviderSubject"/>) is unique, so one Google
/// account can only ever sign in to one SIRI UpSkill account.
/// </summary>
public sealed class USER_EXTERNAL_LOGIN
{
    /// <summary>EF Core materialization only.</summary>
    private USER_EXTERNAL_LOGIN()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public ExternalLoginProvider Provider { get; private set; }

    /// <summary>The provider's stable, opaque user id (Google: the ID token's <c>sub</c> claim).</summary>
    public string ProviderSubject { get; private set; } = string.Empty;

    /// <summary>Email the provider reported when the link was created — informational only, never used
    /// for lookups after linking.</summary>
    public string ProviderEmail { get; private set; } = string.Empty;

    public DateTime LinkedAtUtc { get; private set; }

    public DateTime LastLoginAtUtc { get; private set; }

    public static USER_EXTERNAL_LOGIN Link(
        Guid userId, ExternalLoginProvider provider, string providerSubject, string providerEmail, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerSubject);
        ArgumentException.ThrowIfNullOrWhiteSpace(providerEmail);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.UtcNow;

        return new USER_EXTERNAL_LOGIN
        {
            Id = UuidV7.NewId(),
            UserId = userId,
            Provider = provider,
            ProviderSubject = providerSubject,
            ProviderEmail = providerEmail,
            LinkedAtUtc = now,
            LastLoginAtUtc = now,
        };
    }

    public void RecordLogin(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        LastLoginAtUtc = clock.UtcNow;
    }
}
