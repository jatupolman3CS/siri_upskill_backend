using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.SharedKernel;

namespace Siri.Integrations.Google.Logging;

/// <summary>
/// <b>DEVELOPMENT ONLY.</b> A fake <see cref="IGoogleOAuthService"/> that never contacts Google, so the whole connect flow can be
/// exercised on a laptop with no Google Cloud project. Selected exclusively by the explicit setting <c>Live:Provider=Logging</c>
/// (the selection happens in the Live module); it is never a default and <c>ProductionConfigurationGuard</c> refuses it in Production.
/// <para>
/// Behaviour: <see cref="BuildAuthorizationUrl"/> returns <c>{RedirectUri}?code=dev&amp;state=...</c> (so the "consent screen" redirects
/// straight back to our own callback); the code exchange and refresh return clearly-fake <c>dev-...</c> tokens; userinfo is
/// <c>dev-instructor@example.test</c>. Anything not issued by this fake (a token without the <c>dev-</c> prefix) is rejected with
/// <c>google.unauthorized</c>, so the reconnect paths stay testable.
/// </para>
/// </summary>
public sealed class LoggingGoogleOAuthService : IGoogleOAuthService
{
    public const string DevTokenPrefix = "dev-";

    internal const string DevEmail = "dev-instructor@example.test";

    internal const string DevSubject = "dev-google-subject";

    private readonly GoogleOAuthOptions _options;
    private readonly IClock _clock;

    public LoggingGoogleOAuthService(IOptions<GoogleOAuthOptions> options, IClock clock, ILogger<LoggingGoogleOAuthService> logger)
    {
        _options = options.Value;
        _clock = clock;
        logger.LogWarning(
            "LoggingGoogleOAuthService is active (Live:Provider=Logging): Google OAuth is FAKED. Development only - never use in production.");
    }

    public bool IsConfigured => true;

    public string BuildAuthorizationUrl(string state, string codeChallenge)
    {
        ArgumentException.ThrowIfNullOrEmpty(state);

        if (string.IsNullOrWhiteSpace(_options.RedirectUri))
        {
            throw new InvalidOperationException("Live:Provider=Logging needs Integrations:Google:RedirectUri to point at the local callback.");
        }

        var separator = _options.RedirectUri.Contains('?', StringComparison.Ordinal) ? '&' : '?';
        return $"{_options.RedirectUri}{separator}code=dev&state={Uri.EscapeDataString(state)}";
    }

    public Task<Result<GoogleTokenSet>> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(code))
        {
            return Task.FromResult(Result.Failure<GoogleTokenSet>(GoogleErrors.BadRequest("Fake Google token exchange: a code is required.")));
        }

        return Task.FromResult(Result.Success(Tokens(withRefreshToken: true)));
    }

    public Task<Result<GoogleTokenSet>> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(refreshToken) || !refreshToken.StartsWith(DevTokenPrefix, StringComparison.Ordinal))
        {
            return Task.FromResult(Result.Failure<GoogleTokenSet>(
                GoogleErrors.Unauthorized("Fake Google token refresh: not a dev refresh token.", "invalid_grant")));
        }

        return Task.FromResult(Result.Success(Tokens(withRefreshToken: false)));
    }

    public Task<Result<GoogleUserInfo>> GetUserInfoAsync(string accessToken, CancellationToken ct)
    {
        if (string.IsNullOrEmpty(accessToken) || !accessToken.StartsWith(DevTokenPrefix, StringComparison.Ordinal))
        {
            return Task.FromResult(Result.Failure<GoogleUserInfo>(GoogleErrors.Unauthorized("Fake Google userinfo: not a dev access token.")));
        }

        return Task.FromResult(Result.Success(new GoogleUserInfo(DevSubject, DevEmail, true)));
    }

    public Task<Result> RevokeAsync(string token, CancellationToken ct) => Task.FromResult(Result.Success());

    private GoogleTokenSet Tokens(bool withRefreshToken) => new(
        $"{DevTokenPrefix}access-token",
        _clock.UtcNow.AddHours(1),
        withRefreshToken ? $"{DevTokenPrefix}refresh-token" : null,
        string.Join(' ', _options.GetEffectiveScopes()));
}
