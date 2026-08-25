namespace Siri.Modules.Identity.Features.Login;

/// <summary>
/// JSON response body for POST /api/identity/login. Deliberately carries only the access token and
/// its expiry — never the refresh token (see <see cref="LoginResult"/>'s doc comment for where that
/// goes instead). Per security.md, the access token belongs in the browser's memory only (never
/// localStorage) — this shape is what makes that possible: the frontend reads it straight out of the
/// response body and holds onto it itself, rather than relying on a cookie it can't control the
/// storage of.
/// </summary>
public sealed record LoginResponse(string AccessToken, DateTime AccessTokenExpiresAtUtc);
