namespace Siri.Modules.Identity.Features.Login;

/// <summary>
/// Everything <see cref="LoginHandler"/> produces on a successful login — including the raw refresh
/// token and its expiry. This is <b>not</b> the JSON response type (see <see cref="LoginResponse"/>):
/// <see cref="LoginEndpoint"/> reads <see cref="RawRefreshToken"/>/<see cref="RefreshTokenExpiresAtUtc"/>
/// off this internal record to set the httpOnly cookie, then builds the public
/// <see cref="LoginResponse"/> from only the access-token fields — the raw refresh token itself never
/// reaches anything that gets serialized to the client's JSON body (security.md: refresh token in the
/// cookie only).
/// </summary>
public sealed record LoginResult(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RawRefreshToken,
    DateTime RefreshTokenExpiresAtUtc);
