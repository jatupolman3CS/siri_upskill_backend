using Siri.Modules.Identity.Domain;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>
/// Issues the short-lived JWT access token a caller carries in memory after Login/Refresh (security.md:
/// "Access token JWT 15 นาที ... access token อยู่ใน memory เท่านั้น"). Kept as a narrow, injectable
/// interface — same reasoning as <see cref="ISecurityTokenGenerator"/> and <see cref="IUserPasswordHasher"/>
/// — so handlers never touch JWT libraries directly.
/// </summary>
public interface IAccessTokenGenerator
{
    /// <summary>Builds a signed access token for <paramref name="user"/> (claims: user id, one role
    /// claim per assigned <see cref="Role"/>, and <paramref name="sessionId"/> as the "sid" claim (P0-18
    /// — see <see cref="AccessTokenGenerator"/>'s doc comment for the exact shape/why) and returns it
    /// together with its own expiry, so callers never have to separately recompute "now + lifetime" and
    /// risk it drifting from what's actually inside the token.</summary>
    /// <param name="user">The user the token is issued to.</param>
    /// <param name="sessionId">The <see cref="Domain.UserSession.Id"/> this token was minted for
    /// (Login mints a brand-new one; Refresh passes through the same session id the rotated token
    /// already carried) — embedded so a later request bearing this token can identify "which of my
    /// sessions am I" without a second DB lookup (P0-18's device-management API).</param>
    (string AccessToken, DateTime ExpiresAtUtc) Generate(User user, Guid sessionId);
}
