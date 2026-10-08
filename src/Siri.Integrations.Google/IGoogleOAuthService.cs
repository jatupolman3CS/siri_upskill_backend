using Siri.SharedKernel;

namespace Siri.Integrations.Google;

/// <summary>Result of a token-endpoint call. <see cref="RefreshToken"/> is only present on the first code exchange
/// (and, rarely, on a rotation); a plain refresh normally returns <c>null</c> for it.</summary>
/// <param name="AccessToken">Bearer token. <b>Secret — never log.</b></param>
/// <param name="ExpiresAtUtc">Exact instant Google said the access token expires (now + <c>expires_in</c>); callers should
/// refresh a little before it.</param>
/// <param name="RefreshToken">Long-lived refresh token. <b>Secret — encrypt before storing, never log.</b></param>
/// <param name="GrantedScopes">Space-delimited <c>scope</c> exactly as Google returned it.</param>
public sealed record GoogleTokenSet(string AccessToken, DateTime ExpiresAtUtc, string? RefreshToken, string GrantedScopes);

/// <param name="Subject">Google's stable account id (<c>sub</c>).</param>
/// <param name="Email">The account's e-mail address (shown to the instructor; never logged).</param>
/// <param name="EmailVerified">Whether Google has verified <paramref name="Email"/>.</param>
public sealed record GoogleUserInfo(string Subject, string Email, bool EmailVerified);

/// <summary>
/// Google OAuth 2.0 authorization-code + PKCE client (P11-03 contract section 5, FROZEN). Every failure is a typed
/// <see cref="Result"/> carrying one of the <see cref="GoogleErrors"/> codes — nothing here throws for an expected
/// Google/network failure. There is no central credential: each call acts for one instructor's token.
/// </summary>
public interface IGoogleOAuthService
{
    /// <summary><c>Integrations:Google:ClientId</c> is set (or the dev-only Logging provider is selected).
    /// When false the connect feature is off and every call answers <c>google.not_configured</c>.</summary>
    bool IsConfigured { get; }

    /// <summary>
    /// The Google consent-screen URL: PKCE <c>S256</c>, <c>access_type=offline</c>, <c>prompt=consent</c> (so a refresh
    /// token is always issued), <c>include_granted_scopes=false</c>. Throws <see cref="InvalidOperationException"/> if
    /// <see cref="IsConfigured"/> is false — check it first (the Live service answers 503 <c>live.google_not_configured</c>).
    /// </summary>
    string BuildAuthorizationUrl(string state, string codeChallenge);

    /// <summary>Exchanges the callback <c>code</c> (with the PKCE verifier) for tokens.</summary>
    Task<Result<GoogleTokenSet>> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct);

    /// <summary>Gets a fresh access token from a stored refresh token. <c>google.unauthorized</c> means the refresh token
    /// is no longer usable (revoked/expired) — the instructor must reconnect. <see cref="DomainError.Reason"/> tells
    /// <c>invalid_grant</c> (token dead) from <c>invalid_client</c> (operator misconfiguration — wrong client secret).</summary>
    Task<Result<GoogleTokenSet>> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct);

    /// <summary>OpenID Connect userinfo for the account the <paramref name="accessToken"/> belongs to.</summary>
    Task<Result<GoogleUserInfo>> GetUserInfoAsync(string accessToken, CancellationToken ct);

    /// <summary>Best-effort revoke of an access or refresh token. Success when Google answers 200 or 400 (already
    /// invalid); a failure result is informational — callers proceed regardless.</summary>
    Task<Result> RevokeAsync(string token, CancellationToken ct);
}
