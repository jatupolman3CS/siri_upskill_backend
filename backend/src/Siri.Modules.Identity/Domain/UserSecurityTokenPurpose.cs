namespace Siri.Modules.Identity.Domain;

/// <summary>
/// What a <see cref="UserSecurityToken"/> is for. Stored as a string in the database (see
/// <c>Infrastructure/UserSecurityTokenConfiguration.cs</c>), following the same
/// <c>HasConversion&lt;string&gt;()</c> convention <see cref="UserStatus"/> established as the first
/// enum in the codebase.
/// </summary>
public enum UserSecurityTokenPurpose
{
    /// <summary>Confirms a newly registered account's email address (P0-15).</summary>
    EmailConfirmation,

    /// <summary>
    /// The "forgot password" flow (P0-21): issued by <c>Features/ForgotPassword/Handler.cs</c> (1 hour
    /// expiry — docs/TASKS.md's P0-21 acceptance criterion — much shorter than
    /// <see cref="EmailConfirmation"/>'s 24 hours, since a reset link is the higher-stakes of the two)
    /// and redeemed by <c>Features/ResetPassword/Handler.cs</c>, which also revokes every active
    /// <see cref="UserSession"/>/<see cref="RefreshToken"/> for the account on success — same
    /// one-time-use/hash-only-at-rest mechanism <see cref="EmailConfirmation"/> already established, no
    /// second token scheme was built for this.
    /// </summary>
    PasswordReset,
}
