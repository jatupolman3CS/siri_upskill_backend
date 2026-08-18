namespace Siri.Modules.Identity.Features.Refresh;

/// <summary>Everything <see cref="RefreshHandler"/> produces on a successful rotation — same
/// endpoint-internal-vs-JSON split as <see cref="Login.LoginResult"/>/<see cref="Login.LoginResponse"/>;
/// see that record's doc comment for why the raw token lives here and not on
/// <see cref="RefreshResponse"/>.</summary>
public sealed record RefreshResult(
    string AccessToken,
    DateTime AccessTokenExpiresAtUtc,
    string RawRefreshToken,
    DateTime RefreshTokenExpiresAtUtc);
