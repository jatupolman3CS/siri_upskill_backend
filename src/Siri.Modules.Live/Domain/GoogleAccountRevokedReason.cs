namespace Siri.Modules.Live.Domain;

/// <summary>
/// The only values <see cref="INSTRUCTOR_GOOGLE_ACCOUNT.REVOKED_REASON"/> may hold
/// (docs/contracts/P11-03-live-module-google-meetings.md §2.1). They are also exposed verbatim to the
/// frontend (<c>GoogleConnectionStatusResponse.RevokedReason</c>), so they are stable wire strings.
/// </summary>
public static class GoogleAccountRevokedReason
{
    /// <summary>Google rejected the refresh token (revoked by the user in their Google account, expired
    /// in Testing mode, password change, ...).</summary>
    public const string InvalidGrant = "invalid_grant";

    /// <summary>The instructor pressed "disconnect" in the app.</summary>
    public const string UserDisconnected = "user_disconnected";

    /// <summary>The OAuth callback returned without a Calendar scope.</summary>
    public const string ScopeMissing = "scope_missing";

    /// <summary>Calendar answered 403 because the granted scope is too narrow for the call.</summary>
    public const string InsufficientScope = "insufficient_scope";

    public static bool IsValid(string? reason) =>
        reason is InvalidGrant or UserDisconnected or ScopeMissing or InsufficientScope;
}
