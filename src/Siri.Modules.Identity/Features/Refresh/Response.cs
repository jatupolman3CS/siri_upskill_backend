namespace Siri.Modules.Identity.Features.Refresh;

/// <summary>JSON response body for POST /api/identity/refresh — the newly rotated access token and
/// its expiry only, never the new raw refresh token (set as the httpOnly cookie instead; see
/// <see cref="RefreshResult"/>'s doc comment).</summary>
public sealed record RefreshResponse(string AccessToken, DateTime AccessTokenExpiresAtUtc);
