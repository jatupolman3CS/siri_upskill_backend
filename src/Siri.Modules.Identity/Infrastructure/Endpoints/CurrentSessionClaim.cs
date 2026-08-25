using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;

namespace Siri.Modules.Identity.Infrastructure.Endpoints;

/// <summary>
/// Reads the current request's own session id — the "sid" claim <see cref="AccessTokenGenerator"/>
/// writes into every access token (task P0-18 — see that class's doc comment for why) — back out of the
/// authenticated <see cref="ClaimsPrincipal"/> ASP.NET Core's JwtBearer handler already populated onto
/// <c>HttpContext.User</c> by the time an endpoint here runs.
/// <para>
/// <b>Why this is a session id, not a userId, and why it lives here rather than on
/// <see cref="Siri.SharedKernel.IUserContext"/></b> (task instruction: "think it through and document
/// your reasoning" — this is the design point the task called out explicitly). "Which session is this
/// very request coming from" is a concept only the device-management endpoints in this module
/// (<c>Features/ListSessions</c>, <c>Features/RevokeSession</c>, <c>Features/RevokeOtherSessions</c>,
/// <c>Features/RevokeAllSessions</c>) actually need — every other handler in every other module only
/// ever needs <em>who</em> is calling (<see cref="Siri.SharedKernel.IUserContext.UserId"/>), never
/// <em>which of that user's devices</em>. Broadening the shared, cross-module
/// <see cref="Siri.SharedKernel.IUserContext"/> interface for a concern exactly four endpoints in one
/// module care about would leak an Identity-module-specific detail into a SharedKernel contract every
/// other module also implements/consumes — CLAUDE.md rule 6's "ทำตาม scope ที่สั่ง...ห้าม refactor ข้าม
/// โมดูลโดยไม่ถาม" argues against that. Kept as a small, static, pure function instead — exactly the
/// same shape <see cref="RefreshTokenCookie"/> already uses for "read one specific piece of request data
/// that only this module's endpoints need" — and directly unit-testable against a bare
/// <see cref="ClaimsPrincipal"/> with no HTTP pipeline/DI involved (see
/// <c>tests/Siri.UnitTests/Identity/CurrentSessionClaimTests.cs</c>).
/// </para>
/// </summary>
public static class CurrentSessionClaim
{
    /// <summary>Parses the "sid" claim as a <see cref="Guid"/>, or <c>null</c> if it is missing or
    /// malformed. <c>null</c> is never treated as an error by any caller of this method — a caller with
    /// an otherwise-valid, still-authenticated access token must still be able to list/revoke their own
    /// sessions; it would just never see any of them flagged as "this is the current one" (every access
    /// token this codebase itself issues, from either <see cref="Login.LoginHandler"/> or
    /// <see cref="Refresh.RefreshHandler"/>, always carries this claim — see
    /// <see cref="AccessTokenGenerator"/> — so in practice this only returns <c>null</c> for a
    /// hand-crafted or otherwise foreign token, not a real one this system minted).</summary>
    public static Guid? Read(ClaimsPrincipal principal)
    {
        ArgumentNullException.ThrowIfNull(principal);

        var value = principal.FindFirstValue(JwtRegisteredClaimNames.Sid);
        return Guid.TryParse(value, out var sessionId) ? sessionId : null;
    }
}
