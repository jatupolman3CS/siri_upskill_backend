using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Tokens;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>What a verified Google ID token says about the person signing in. <paramref name="Picture"/>
/// is the token's optional <c>picture</c> claim (profile image URL) — passed through untouched, so the
/// consumer must still decide whether it is acceptable (see <c>USER.SetAvatarIfMissing</c>).</summary>
public sealed record GoogleIdentity(string Subject, string Email, bool EmailVerified, string? Name, string? Picture = null);

public interface IGoogleIdTokenVerifier
{
    /// <summary>Returns the identity inside <paramref name="idToken"/>, or <c>null</c> when the token is
    /// malformed, expired, signed by anyone but Google, or issued to a different OAuth client.</summary>
    Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken cancellationToken);
}

/// <summary>
/// Verifies Google Identity Services ID tokens: RS256 signature against the keys published by the
/// discovery document's <c>jwks_uri</c>, <c>iss</c>, <c>aud</c> (must equal the configured client id)
/// and lifetime. Keys are cached for an hour and refetched (at most every few minutes) when a token
/// is signed with a key we do not hold yet, which is how Google rotates them.
/// </summary>
public sealed class GoogleIdTokenVerifier(
    IHttpClientFactory httpClientFactory,
    IOptions<GoogleLoginOptions> options,
    IClock clock,
    ILogger<GoogleIdTokenVerifier> logger) : IGoogleIdTokenVerifier
{
    /// <summary>Named <see cref="HttpClient"/> registered by <see cref="IdentityModule"/>.</summary>
    public const string HttpClientName = "google-oidc";

    private const int MaxTokenLength = 4096;
    private static readonly TimeSpan KeyCacheLifetime = TimeSpan.FromHours(1);
    private static readonly TimeSpan MinKeyRefreshInterval = TimeSpan.FromMinutes(2);
    private static readonly TimeSpan ClockSkew = TimeSpan.FromMinutes(2);

    // Google documents both spellings of the issuer for ID tokens.
    private static readonly string[] GoogleIssuers = ["https://accounts.google.com", "accounts.google.com"];

    private readonly SemaphoreSlim _refreshLock = new(1, 1);
    private KeyMaterial? _keys;

    public async Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken cancellationToken)
    {
        var settings = options.Value;
        if (!settings.IsEnabled || string.IsNullOrWhiteSpace(idToken) || idToken.Length > MaxTokenLength)
        {
            return null;
        }

        try
        {
            var keys = await GetKeysAsync(forceRefresh: false, cancellationToken).ConfigureAwait(false);
            var result = await ValidateAsync(idToken, keys, settings).ConfigureAwait(false);

            if (!result.IsValid && IsUnknownKeyFailure(result.Exception))
            {
                // Possibly a freshly rotated Google key — look once more with fresh key material.
                keys = await GetKeysAsync(forceRefresh: true, cancellationToken).ConfigureAwait(false);
                result = await ValidateAsync(idToken, keys, settings).ConfigureAwait(false);
            }

            if (!result.IsValid)
            {
                logger.LogInformation("Google ID token rejected: {Reason}", result.Exception?.GetType().Name ?? "invalid");
                return null;
            }

            return ReadIdentity(result.Claims);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or InvalidOperationException)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                throw;
            }

            // Google's keys could not be fetched/parsed. Treated as "token not verified" — never accepted.
            logger.LogWarning(ex, "Could not verify Google ID token (key material unavailable).");
            return null;
        }
    }

    private static bool IsUnknownKeyFailure(Exception? exception) =>
        exception is SecurityTokenSignatureKeyNotFoundException or SecurityTokenInvalidSignatureException;

    private async Task<TokenValidationResult> ValidateAsync(string idToken, KeyMaterial keys, GoogleLoginOptions settings)
    {
        var parameters = new TokenValidationParameters
        {
            ValidIssuers = [keys.Issuer, .. GoogleIssuers],
            ValidateIssuer = true,
            ValidAudience = settings.ClientId,
            ValidateAudience = true,
            IssuerSigningKeys = keys.Jwks.GetSigningKeys(),
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            ValidAlgorithms = [SecurityAlgorithms.RsaSha256],
            ValidateLifetime = true,
            RequireExpirationTime = true,
            // Lifetime is judged against the injected clock (not the machine clock) so expiry is testable.
            LifetimeValidator = (notBefore, expires, _, _) =>
            {
                var now = clock.UtcNow;
                if (expires is null || expires.Value.ToUniversalTime() <= now - ClockSkew)
                {
                    return false;
                }

                return notBefore is null || notBefore.Value.ToUniversalTime() <= now + ClockSkew;
            },
        };

        return await new JsonWebTokenHandler().ValidateTokenAsync(idToken, parameters).ConfigureAwait(false);
    }

    private static GoogleIdentity? ReadIdentity(IDictionary<string, object> claims)
    {
        var subject = ReadString(claims, "sub");
        var email = ReadString(claims, "email");
        if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var emailVerified = claims.TryGetValue("email_verified", out var raw)
            && raw switch
            {
                bool b => b,
                string s => bool.TryParse(s, out var parsed) && parsed,
                _ => false,
            };

        return new GoogleIdentity(subject, email, emailVerified, ReadString(claims, "name"), ReadString(claims, "picture"));
    }

    private static string? ReadString(IDictionary<string, object> claims, string name) =>
        claims.TryGetValue(name, out var value) ? value as string : null;

    private async Task<KeyMaterial> GetKeysAsync(bool forceRefresh, CancellationToken cancellationToken)
    {
        var current = _keys;
        if (current is not null && IsUsable(current, forceRefresh))
        {
            return current;
        }

        await _refreshLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            current = _keys;
            if (current is not null && IsUsable(current, forceRefresh))
            {
                return current;
            }

            var fetched = await FetchKeysAsync(cancellationToken).ConfigureAwait(false);
            _keys = fetched;
            return fetched;
        }
        finally
        {
            _refreshLock.Release();
        }
    }

    private bool IsUsable(KeyMaterial keys, bool forceRefresh)
    {
        var age = clock.UtcNow - keys.FetchedAtUtc;
        return forceRefresh ? age < MinKeyRefreshInterval : age < KeyCacheLifetime;
    }

    private async Task<KeyMaterial> FetchKeysAsync(CancellationToken cancellationToken)
    {
        using var client = httpClientFactory.CreateClient(HttpClientName);

        var discoveryJson = await client.GetStringAsync(options.Value.DiscoveryUrl, cancellationToken).ConfigureAwait(false);
        using var discovery = JsonDocument.Parse(discoveryJson);
        var root = discovery.RootElement;

        var issuer = root.GetProperty("issuer").GetString();
        var jwksUri = root.GetProperty("jwks_uri").GetString();
        if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(jwksUri))
        {
            throw new InvalidOperationException("Google discovery document is missing issuer or jwks_uri.");
        }

        var jwksJson = await client.GetStringAsync(jwksUri, cancellationToken).ConfigureAwait(false);

        return new KeyMaterial(issuer, new JsonWebKeySet(jwksJson), clock.UtcNow);
    }

    private sealed record KeyMaterial(string Issuer, JsonWebKeySet Jwks, DateTime FetchedAtUtc);
}
