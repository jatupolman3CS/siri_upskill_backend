using Siri.Integrations.Google;
using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Live.Domain;

/// <summary>
/// An instructor's connected Google account (OAuth refresh token) — the credential Live uses to create
/// Calendar events with a Google Meet conference on the instructor's own calendar
/// (docs/contracts/P11-03-live-module-google-meetings.md §2.1, Q10=B: there is no platform-wide Google
/// credential). One instructor = at most one row (<see cref="INSTRUCTOR_USER_ID"/> is UNIQUE); a
/// disconnect/revocation clears the token but keeps the row, so reconnecting reuses it
/// (<see cref="Reconnect"/>).
/// <para>
/// <b>UPPERCASE naming (docs/DECISIONS.md D-17):</b> entity/property names are UPPERCASE and map 1:1 to
/// UPPER_SNAKE_CASE columns, except the four <see cref="IAuditable"/> properties, which must stay
/// PascalCase — <c>AuditableEntityInterceptor</c> looks them up by C# name (see
/// <c>Siri.Modules.Community.Domain.DISCUSSION</c>'s doc comment for the full reasoning).
/// </para>
/// <para>
/// <b>Secret handling:</b> <see cref="REFRESH_TOKEN_ENCRYPTED"/> only ever holds the output of
/// <c>ISensitiveDataProtector.Encrypt</c> — encrypting/decrypting is the application service's job (the
/// domain never sees plaintext and must never log it). It is <c>null</c> once the account is revoked: a
/// token that no longer works is not worth keeping.
/// </para>
/// <para>
/// No FK to <c>identity.Users</c> (<see cref="INSTRUCTOR_USER_ID"/>) — cross-module/cross-schema, same
/// reasoning as <c>Siri.Modules.Learning.Domain.ENROLLMENT.USER_ID</c>.
/// </para>
/// </summary>
public sealed class INSTRUCTOR_GOOGLE_ACCOUNT : IAuditable
{
    private const int GoogleSubjectMaxLength = 64;
    private const int GoogleEmailMaxLength = 320;
    private const int ScopesMaxLength = 500;
    private const int HostedDomainMaxLength = 255;

    /// <summary>EF Core materialization only.</summary>
    private INSTRUCTOR_GOOGLE_ACCOUNT()
    {
    }

    public Guid INSTRUCTOR_GOOGLE_ACCOUNT_ID { get; private set; }

    /// <summary><c>identity.Users.Id</c> of the instructor. No FK; UNIQUE (one Google account per instructor).</summary>
    public Guid INSTRUCTOR_USER_ID { get; private set; }

    /// <summary>The <c>sub</c> claim from Google's userinfo endpoint.</summary>
    public string GOOGLE_SUBJECT { get; private set; } = string.Empty;

    /// <summary>Shown in the UI so the instructor can see which Google account is connected.</summary>
    public string GOOGLE_EMAIL { get; private set; } = string.Empty;

    /// <summary><c>ISensitiveDataProtector.Encrypt(refreshToken)</c>; <c>null</c> after
    /// <see cref="MarkRevoked"/>. Never the plaintext token.</summary>
    public string? REFRESH_TOKEN_ENCRYPTED { get; private set; }

    /// <summary>The scopes Google actually granted (space-separated, as Google returns them).</summary>
    public string SCOPES { get; private set; } = string.Empty;

    public DateTime CONNECTED_AT_UTC { get; private set; }

    /// <summary>Last time a refresh-token exchange succeeded.</summary>
    public DateTime? LAST_VALIDATED_AT_UTC { get; private set; }

    public DateTime? REVOKED_AT_UTC { get; private set; }

    /// <summary>One of <see cref="GoogleAccountRevokedReason"/>'s values, or <c>null</c> while active.</summary>
    public string? REVOKED_REASON { get; private set; }

    /// <summary>The Google Workspace domain (userinfo <c>hd</c>), stored at every connect/reconnect (P11-13 contract section 3). <c>null</c> = a
    /// personal Google account — <em>if</em> <see cref="ACCOUNT_KIND_CHECKED_AT_UTC"/> is set; together with a <c>null</c> check time it means
    /// "never looked at" (a row connected before the column existed). Normalised: trimmed, lower-case. Not a secret.</summary>
    public string? HOSTED_DOMAIN { get; private set; }

    /// <summary>When <see cref="HOSTED_DOMAIN"/> was last read from Google. <c>null</c> = never (see <see cref="AccountKind"/>).</summary>
    public DateTime? ACCOUNT_KIND_CHECKED_AT_UTC { get; private set; }

    /// <summary>EF concurrency token, rotated by <c>ConcurrencyTokenInterceptor</c>.</summary>
    public byte[] ROW_VERSION { get; private set; } = [];

    /// <summary>True while a usable refresh token is held. Derived — not mapped (no setter).</summary>
    public bool IsActive => REFRESH_TOKEN_ENCRYPTED != null && REVOKED_AT_UTC == null;

    /// <summary><see cref="GoogleAccountKind.Workspace"/> when a hosted domain is stored, <see cref="GoogleAccountKind.Personal"/> when the check ran and
    /// found none, <see cref="GoogleAccountKind.Unknown"/> when it never ran. Derived — not mapped.</summary>
    public GoogleAccountKind AccountKind =>
        !string.IsNullOrEmpty(HOSTED_DOMAIN) ? GoogleAccountKind.Workspace
        : ACCOUNT_KIND_CHECKED_AT_UTC is null ? GoogleAccountKind.Unknown
        : GoogleAccountKind.Personal;

    /// <summary>The granted <see cref="SCOPES"/> include <b>both</b> recording scopes (<c>meetings.space.readonly</c> and <c>drive.meet.readonly</c>).
    /// Derived — not mapped.</summary>
    public bool HasRecordingScopes => GoogleScopes.HasRecordingScopes(SCOPES);

    // ---- IAuditable (stays PascalCase — see this class's doc comment) ---------------------------
    public DateTime CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    DateTime IAuditable.CreatedAtUtc
    {
        get => CreatedAtUtc;
        set => CreatedAtUtc = value;
    }

    Guid? IAuditable.CreatedBy
    {
        get => CreatedBy;
        set => CreatedBy = value;
    }

    DateTime? IAuditable.UpdatedAtUtc
    {
        get => UpdatedAtUtc;
        set => UpdatedAtUtc = value;
    }

    Guid? IAuditable.UpdatedBy
    {
        get => UpdatedBy;
        set => UpdatedBy = value;
    }

    /// <summary>Connects an instructor's Google account for the first time. <paramref name="hostedDomain"/> is the userinfo <c>hd</c> claim read in the same
    /// round trip (<c>null</c> = a personal account); the check time is stamped now.</summary>
    public static INSTRUCTOR_GOOGLE_ACCOUNT Connect(
        Guid instructorUserId, string googleSubject, string googleEmail, string refreshTokenEncrypted, string scopes, IClock clock, string? hostedDomain = null)
    {
        if (instructorUserId == Guid.Empty)
        {
            throw new ArgumentException("Instructor user ID cannot be empty.", nameof(instructorUserId));
        }

        ArgumentNullException.ThrowIfNull(clock);

        var account = new INSTRUCTOR_GOOGLE_ACCOUNT
        {
            INSTRUCTOR_GOOGLE_ACCOUNT_ID = UuidV7.NewId(),
            INSTRUCTOR_USER_ID = instructorUserId,
        };

        account.ApplyConnection(googleSubject, googleEmail, refreshTokenEncrypted, scopes, hostedDomain, clock);
        return account;
    }

    /// <summary>Re-connects after a disconnect/revocation (or replaces the credential of a still-active
    /// account): swaps in the new identity/token/scopes/hosted domain, clears every <c>REVOKED_*</c> field and the stale
    /// <see cref="LAST_VALIDATED_AT_UTC"/> (the new token has not been through a refresh yet), and restamps
    /// <see cref="CONNECTED_AT_UTC"/> and <see cref="ACCOUNT_KIND_CHECKED_AT_UTC"/>.</summary>
    public void Reconnect(string googleSubject, string googleEmail, string refreshTokenEncrypted, string scopes, IClock clock, string? hostedDomain = null)
    {
        ArgumentNullException.ThrowIfNull(clock);

        ApplyConnection(googleSubject, googleEmail, refreshTokenEncrypted, scopes, hostedDomain, clock);
    }

    /// <summary>
    /// Records the result of the best-effort lookup that fills in a row connected before the account-kind check existed (P11-13): the userinfo
    /// <c>hd</c> claim (<c>null</c> = a personal account) and the time it was read. A no-op when the kind was already checked, so the value stored by a
    /// real connect is never overwritten by a late lookup.
    /// </summary>
    public void ResolveAccountKind(string? hostedDomain, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (ACCOUNT_KIND_CHECKED_AT_UTC is not null)
        {
            return;
        }

        HOSTED_DOMAIN = NormalizeHostedDomain(hostedDomain);
        ACCOUNT_KIND_CHECKED_AT_UTC = clock.UtcNow;
    }

    /// <summary>A refresh-token exchange succeeded just now.</summary>
    public void MarkValidated(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        LAST_VALIDATED_AT_UTC = clock.UtcNow;
    }

    /// <summary>
    /// The credential can no longer be used. Clears <see cref="REFRESH_TOKEN_ENCRYPTED"/> (a dead token
    /// is never retained) and records why. Idempotent: an already-revoked account keeps its original
    /// reason/time, so the "alert the instructor once per revocation" rule can rely on <see cref="IsActive"/>
    /// flipping exactly once.
    /// </summary>
    public void MarkRevoked(string reason, IClock clock)
    {
        if (!GoogleAccountRevokedReason.IsValid(reason))
        {
            throw new ArgumentException($"'{reason}' is not a known revocation reason.", nameof(reason));
        }

        ArgumentNullException.ThrowIfNull(clock);

        if (REVOKED_AT_UTC is not null)
        {
            return;
        }

        REFRESH_TOKEN_ENCRYPTED = null;
        REVOKED_AT_UTC = clock.UtcNow;
        REVOKED_REASON = reason;
    }

    private static string? NormalizeHostedDomain(string? hostedDomain)
    {
        if (string.IsNullOrWhiteSpace(hostedDomain))
        {
            return null;
        }

        var normalized = hostedDomain.Trim().ToLowerInvariant();
        if (normalized.Length > HostedDomainMaxLength)
        {
            throw new ArgumentException($"Hosted domain must be at most {HostedDomainMaxLength} characters.", nameof(hostedDomain));
        }

        return normalized;
    }

    private void ApplyConnection(string googleSubject, string googleEmail, string refreshTokenEncrypted, string scopes, string? hostedDomain, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(googleSubject);
        ArgumentException.ThrowIfNullOrWhiteSpace(googleEmail);
        ArgumentException.ThrowIfNullOrWhiteSpace(refreshTokenEncrypted);
        ArgumentException.ThrowIfNullOrWhiteSpace(scopes);

        if (googleSubject.Length > GoogleSubjectMaxLength)
        {
            throw new ArgumentException($"Google subject must be at most {GoogleSubjectMaxLength} characters.", nameof(googleSubject));
        }

        if (googleEmail.Length > GoogleEmailMaxLength)
        {
            throw new ArgumentException($"Google email must be at most {GoogleEmailMaxLength} characters.", nameof(googleEmail));
        }

        if (scopes.Length > ScopesMaxLength)
        {
            throw new ArgumentException($"Scopes must be at most {ScopesMaxLength} characters.", nameof(scopes));
        }

        var normalizedDomain = NormalizeHostedDomain(hostedDomain);

        GOOGLE_SUBJECT = googleSubject;
        GOOGLE_EMAIL = googleEmail;
        REFRESH_TOKEN_ENCRYPTED = refreshTokenEncrypted;
        SCOPES = scopes;
        HOSTED_DOMAIN = normalizedDomain;
        ACCOUNT_KIND_CHECKED_AT_UTC = clock.UtcNow;
        CONNECTED_AT_UTC = clock.UtcNow;
        LAST_VALIDATED_AT_UTC = null;
        REVOKED_AT_UTC = null;
        REVOKED_REASON = null;
    }
}
