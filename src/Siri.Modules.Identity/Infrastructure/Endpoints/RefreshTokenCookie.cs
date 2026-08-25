using Microsoft.AspNetCore.Http;

namespace Siri.Modules.Identity.Infrastructure.Endpoints;

/// <summary>
/// Reads/writes the raw refresh-token cookie shared by Login's and Refresh's endpoints — the one
/// place the exact cookie name/options live, so both endpoints (and any later Logout endpoint) always
/// agree on how to find it. httpOnly + Secure + SameSite=Strict per security.md ("Refresh token เก็บใน
/// httpOnly + Secure + SameSite=Strict cookie") — never readable from JavaScript, only ever sent back
/// to this origin's own top-level requests. <see cref="CookiePath"/> scopes it to
/// <c>/api/identity</c> (rather than the whole site) as defense in depth: the browser simply never
/// attaches this cookie to requests for any other endpoint, so it cannot leak there even by accident.
/// </summary>
internal static class RefreshTokenCookie
{
    private const string CookieName = "siri_refresh_token";
    private const string CookiePath = "/api/identity";

    /// <summary>Sets/replaces the cookie — used by Login (new session) and Refresh (rotation).</summary>
    internal static void Set(HttpContext httpContext, string rawToken, DateTime expiresAtUtc)
    {
        httpContext.Response.Cookies.Append(CookieName, rawToken, new CookieOptions
        {
            HttpOnly = true,
            Secure = true,
            SameSite = SameSiteMode.Strict,
            Path = CookiePath,
            Expires = new DateTimeOffset(DateTime.SpecifyKind(expiresAtUtc, DateTimeKind.Utc)),
        });
    }

    /// <summary>Reads the raw token, or <c>null</c> if the caller sent no cookie at all — Refresh's
    /// handler treats that identically to any other invalid-token rejection (never a distinguishable
    /// "you forgot the cookie" response).</summary>
    internal static string? Read(HttpContext httpContext) =>
        httpContext.Request.Cookies.TryGetValue(CookieName, out var value) ? value : null;
}
