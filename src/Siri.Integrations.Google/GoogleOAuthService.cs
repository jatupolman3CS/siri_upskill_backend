using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.SharedKernel;

namespace Siri.Integrations.Google;

/// <summary>
/// <see cref="IGoogleOAuthService"/> over plain <see cref="HttpClient"/> against Google's documented OAuth 2.0 web-server
/// flow (https://developers.google.com/identity/protocols/oauth2/web-server). No Google SDK.
/// <para>
/// The client secret, authorization code, PKCE verifier and tokens travel only in form bodies / the Authorization header and
/// are never logged. A fresh request message is built per call; there is no in-process retry (the Live sync job owns backoff).
/// </para>
/// </summary>
public sealed class GoogleOAuthService : IGoogleOAuthService
{
    /// <summary>Named <see cref="HttpClient"/> registered by <c>AddGoogleIntegration</c>.</summary>
    public const string HttpClientName = "google-oauth";

    internal const string AuthorizationEndpoint = "https://accounts.google.com/o/oauth2/v2/auth";
    internal const string TokenEndpoint = "https://oauth2.googleapis.com/token";
    internal const string RevokeEndpoint = "https://oauth2.googleapis.com/revoke";
    internal const string UserInfoEndpoint = "https://openidconnect.googleapis.com/v1/userinfo";

    /// <summary>Used when Google omits <c>expires_in</c> (it never has, but a missing member must not mean "never expires").</summary>
    private const int FallbackAccessTokenLifetimeSeconds = 3600;

    /// <summary>Upper bound of remembered access tokens (see <see cref="RememberHostedDomain"/>); the table is cleared if it ever grows past it.</summary>
    private const int MaxRememberedHostedDomains = 1000;

    // A DNS name (lower-case letters, digits, dots, hyphens - punycode included). Anything else in an hd claim is not stored.
    private static readonly Regex HostedDomainPattern =
        new(@"^[a-z0-9]([a-z0-9.-]{0,253}[a-z0-9])?\z", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>
    /// <c>hd</c> claims read from the <c>id_token</c> of the token responses this instance issued, keyed by a hash of the access token they
    /// came with, until that token expires. Google documents <c>hd</c> in the ID token; the userinfo endpoint is expected to return it too but
    /// that is not documented, so <see cref="GetUserInfoAsync"/> falls back to this when the userinfo body has no <c>hd</c>. Process-local and
    /// best effort: a miss just means "personal" as before. Holds no token, only a hash and a domain name.
    /// </summary>
    private readonly ConcurrentDictionary<string, (string? HostedDomain, DateTime ExpiresAtUtc)> _hostedDomainByTokenHash = new(StringComparer.Ordinal);

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly GoogleOAuthOptions _options;
    private readonly IClock _clock;
    private readonly ILogger<GoogleOAuthService> _logger;

    public GoogleOAuthService(
        IHttpClientFactory httpClientFactory,
        IOptions<GoogleOAuthOptions> options,
        IClock clock,
        ILogger<GoogleOAuthService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _options = options.Value;
        _clock = clock;
        _logger = logger;
    }

    public bool IsConfigured => _options.IsConfigured;

    public string BuildAuthorizationUrl(string state, string codeChallenge) =>
        BuildUrl(state, codeChallenge, _options.GetEffectiveScopes(), includeGrantedScopes: false);

    public string BuildRecordingAccessAuthorizationUrl(string state, string codeChallenge) =>
        BuildUrl(
            state,
            codeChallenge,
            _options.GetEffectiveScopes().Concat(GoogleScopes.RecordingScopes).Distinct(StringComparer.Ordinal).ToArray(),
            includeGrantedScopes: true);

    private string BuildUrl(string state, string codeChallenge, IReadOnlyList<string> scopes, bool includeGrantedScopes)
    {
        ArgumentException.ThrowIfNullOrEmpty(state);
        ArgumentException.ThrowIfNullOrEmpty(codeChallenge);

        if (!IsConfigured)
        {
            throw new InvalidOperationException("Google OAuth is not configured (Integrations:Google:ClientId is empty).");
        }

        var parameters = new (string Name, string Value)[]
        {
            ("client_id", _options.ClientId),
            ("redirect_uri", _options.RedirectUri),
            ("response_type", "code"),
            ("scope", string.Join(' ', scopes)),
            ("state", state),
            ("code_challenge", codeChallenge),
            ("code_challenge_method", "S256"),
            ("access_type", "offline"),
            ("prompt", "consent"),
            ("include_granted_scopes", includeGrantedScopes ? "true" : "false"),
        };

        var query = string.Join('&', parameters.Select(p => $"{p.Name}={Uri.EscapeDataString(p.Value)}"));
        return $"{AuthorizationEndpoint}?{query}";
    }

    public Task<Result<GoogleTokenSet>> ExchangeCodeAsync(string code, string codeVerifier, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return Task.FromResult(Result.Failure<GoogleTokenSet>(GoogleErrors.NotConfigured()));
        }

        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(codeVerifier))
        {
            return Task.FromResult(Result.Failure<GoogleTokenSet>(
                GoogleErrors.BadRequest("Google token exchange was not attempted: code and code verifier are required.")));
        }

        return RequestTokensAsync(
            "token exchange",
            [
                new("code", code),
                new("client_id", _options.ClientId),
                new("client_secret", _options.ClientSecret),
                new("redirect_uri", _options.RedirectUri),
                new("grant_type", "authorization_code"),
                new("code_verifier", codeVerifier),
            ],
            ct);
    }

    public Task<Result<GoogleTokenSet>> RefreshAccessTokenAsync(string refreshToken, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return Task.FromResult(Result.Failure<GoogleTokenSet>(GoogleErrors.NotConfigured()));
        }

        if (string.IsNullOrEmpty(refreshToken))
        {
            return Task.FromResult(Result.Failure<GoogleTokenSet>(
                GoogleErrors.Unauthorized("Google token refresh was not attempted: there is no refresh token.", "invalid_grant")));
        }

        return RequestTokensAsync(
            "token refresh",
            [
                new("refresh_token", refreshToken),
                new("client_id", _options.ClientId),
                new("client_secret", _options.ClientSecret),
                new("grant_type", "refresh_token"),
            ],
            ct);
    }

    public async Task<Result<GoogleUserInfo>> GetUserInfoAsync(string accessToken, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return Result.Failure<GoogleUserInfo>(GoogleErrors.NotConfigured());
        }

        const string operation = "userinfo";
        if (!GoogleHttp.IsUsableToken(accessToken))
        {
            return GoogleHttp.InvalidToken<GoogleUserInfo>(operation);
        }

        return await GoogleHttp.SendAsync(
            _httpClientFactory,
            HttpClientName,
            _logger,
            operation,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Get, UserInfoEndpoint);
                request.Headers.Accept.Add(new("application/json"));
                GoogleHttp.SetBearer(request, accessToken);
                return request;
            },
            async (response, token) =>
            {
                if (!response.IsSuccessStatusCode)
                {
                    var error = await GoogleHttp.MapCalendarErrorAsync(response, _logger, operation, token).ConfigureAwait(false);
                    // A Forbidden on userinfo is always "this token cannot read the profile" — same remedy as 401.
                    return Result.Failure<GoogleUserInfo>(
                        response.StatusCode == HttpStatusCode.Forbidden && error.Code != GoogleErrors.RateLimitedCode
                            ? GoogleErrors.Unauthorized(error.Message, error.Reason)
                            : error);
                }

                var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)) as JsonObject;
                var subject = GoogleHttp.GetString(json?["sub"]);
                var email = GoogleHttp.GetString(json?["email"]);
                if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email))
                {
                    // Granted scopes lacked "email"/"openid" (or Google changed the shape): nothing usable to connect.
                    return Result.Failure<GoogleUserInfo>(
                        GoogleErrors.BadRequest("Google userinfo did not include a subject and e-mail address."));
                }

                var hostedDomain = NormalizeHostedDomain(GoogleHttp.GetString(json!["hd"])) ?? LookupRememberedHostedDomain(accessToken);
                return Result.Success(new GoogleUserInfo(subject, email, ReadEmailVerified(json["email_verified"]), hostedDomain));
            },
            ct).ConfigureAwait(false);
    }

    public async Task<Result> RevokeAsync(string token, CancellationToken ct)
    {
        if (!IsConfigured)
        {
            return Result.Failure(GoogleErrors.NotConfigured());
        }

        if (string.IsNullOrEmpty(token))
        {
            return Result.Success();
        }

        const string operation = "token revoke";
        return await GoogleHttp.SendAsync(
            _httpClientFactory,
            HttpClientName,
            _logger,
            operation,
            () => new HttpRequestMessage(HttpMethod.Post, RevokeEndpoint)
            {
                Content = new FormUrlEncodedContent([new("token", token)]),
            },
            async (response, cancellation) =>
            {
                // 200 = revoked, 400 = already invalid/revoked. Both mean "this token is dead", which is the goal.
                if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.BadRequest)
                {
                    return Result.Success();
                }

                return Result.Failure(await GoogleHttp.MapTokenErrorAsync(response, _logger, operation, cancellation).ConfigureAwait(false));
            },
            ct).ConfigureAwait(false);
    }

    private Task<Result<GoogleTokenSet>> RequestTokensAsync(
        string operation,
        KeyValuePair<string, string>[] form,
        CancellationToken ct) =>
        GoogleHttp.SendAsync(
            _httpClientFactory,
            HttpClientName,
            _logger,
            operation,
            () =>
            {
                var request = new HttpRequestMessage(HttpMethod.Post, TokenEndpoint)
                {
                    Content = new FormUrlEncodedContent(form),
                };
                request.Headers.Accept.Add(new("application/json"));
                return request;
            },
            async (response, token) =>
            {
                if (!response.IsSuccessStatusCode)
                {
                    return Result.Failure<GoogleTokenSet>(
                        await GoogleHttp.MapTokenErrorAsync(response, _logger, operation, token).ConfigureAwait(false));
                }

                var body = await response.Content.ReadFromJsonAsync<GoogleTokenResponse>(GoogleJson.Options, token).ConfigureAwait(false);
                if (body is null || string.IsNullOrEmpty(body.AccessToken))
                {
                    _logger.LogWarning("Google {Operation} returned 200 without an access token.", operation);
                    return Result.Failure<GoogleTokenSet>(GoogleErrors.Transient($"Google {operation} returned an unexpected response."));
                }

                var lifetimeSeconds = body.ExpiresIn is > 0 ? body.ExpiresIn.Value : FallbackAccessTokenLifetimeSeconds;
                var expiresAtUtc = _clock.UtcNow.AddSeconds(lifetimeSeconds);
                RememberHostedDomain(body.AccessToken, body.IdToken, expiresAtUtc);
                return Result.Success(new GoogleTokenSet(
                    body.AccessToken,
                    expiresAtUtc,
                    string.IsNullOrEmpty(body.RefreshToken) ? null : body.RefreshToken,
                    body.Scope ?? string.Empty));
            },
            ct);

    /// <summary>Trims and lower-cases an <c>hd</c> value; <c>null</c> when empty or not a plausible domain name (never stored then).</summary>
    internal static string? NormalizeHostedDomain(string? value)
    {
        var domain = value?.Trim().ToLowerInvariant();
        return !string.IsNullOrEmpty(domain) && HostedDomainPattern.IsMatch(domain) ? domain : null;
    }

    /// <summary>Reads <c>hd</c> out of an ID token's payload. The token came straight from Google's token endpoint over TLS, which OpenID Connect
    /// Core accepts in place of a signature check; it is used only to label the account (never for access control). Any malformed token gives <c>null</c>.</summary>
    internal static string? ReadHostedDomainFromIdToken(string? idToken)
    {
        if (string.IsNullOrEmpty(idToken))
        {
            return null;
        }

        var parts = idToken.Split('.');
        if (parts.Length != 3)
        {
            return null;
        }

        try
        {
            var payload = parts[1].Replace('-', '+').Replace('_', '/');
            payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
            var json = JsonNode.Parse(Encoding.UTF8.GetString(Convert.FromBase64String(payload))) as JsonObject;
            return NormalizeHostedDomain(GoogleHttp.GetString(json?["hd"]));
        }
        catch (Exception ex) when (ex is FormatException or System.Text.Json.JsonException or ArgumentException)
        {
            return null;
        }
    }

    private void RememberHostedDomain(string accessToken, string? idToken, DateTime expiresAtUtc)
    {
        var hostedDomain = ReadHostedDomainFromIdToken(idToken);

        // Drop what has expired, and everything if the table somehow grew without bound (a long-running worker minting tokens all day).
        var now = _clock.UtcNow;
        foreach (var entry in _hostedDomainByTokenHash)
        {
            if (entry.Value.ExpiresAtUtc <= now)
            {
                _hostedDomainByTokenHash.TryRemove(entry.Key, out _);
            }
        }

        if (_hostedDomainByTokenHash.Count >= MaxRememberedHostedDomains)
        {
            _hostedDomainByTokenHash.Clear();
        }

        if (hostedDomain is not null)
        {
            _hostedDomainByTokenHash[HashToken(accessToken)] = (hostedDomain, expiresAtUtc);
        }
    }

    private string? LookupRememberedHostedDomain(string accessToken) =>
        _hostedDomainByTokenHash.TryGetValue(HashToken(accessToken), out var entry) && entry.ExpiresAtUtc > _clock.UtcNow
            ? entry.HostedDomain
            : null;

    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    /// <summary>Google sends a JSON boolean; tolerate the string form some OIDC providers use.</summary>
    private static bool ReadEmailVerified(JsonNode? node) =>
        node is JsonValue value
        && ((value.TryGetValue<bool>(out var flag) && flag)
            || (value.TryGetValue<string>(out var text) && bool.TryParse(text, out var parsed) && parsed));
}
