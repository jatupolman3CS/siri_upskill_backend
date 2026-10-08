using System.Security.Cryptography;
using System.Text;

namespace Siri.Integrations.Google;

/// <summary>
/// PKCE (RFC 7636, method <c>S256</c>) and OAuth <c>state</c> helpers for the authorization-code flow. Pure functions
/// over a cryptographically secure RNG — no I/O, no state — so the Live module's connect/callback services share one
/// implementation and the unit tests can pin it to the RFC's own test vector.
/// </summary>
public static class GoogleOAuthPkce
{
    /// <summary>RFC 7636 section 4.1 "unreserved" alphabet for a code verifier.</summary>
    private const string UnreservedCharacters = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789-._~";

    /// <summary>Contract: 64 unreserved characters (the RFC allows 43-128).</summary>
    public const int CodeVerifierLength = 64;

    /// <summary>Contract: 32 random bytes, base64url-encoded without padding (43 characters).</summary>
    public const int StateByteLength = 32;

    /// <summary>New unguessable <c>state</c> value: 32 random bytes as base64url (no padding).</summary>
    public static string GenerateState() =>
        Base64UrlEncode(RandomNumberGenerator.GetBytes(StateByteLength));

    /// <summary>New PKCE code verifier: <see cref="CodeVerifierLength"/> characters drawn uniformly from the RFC 7636
    /// unreserved set. Kept server-side (Redis) and sent only to Google's token endpoint.</summary>
    public static string GenerateCodeVerifier() =>
        RandomNumberGenerator.GetString(UnreservedCharacters, CodeVerifierLength);

    /// <summary><c>code_challenge</c> for <c>code_challenge_method=S256</c>:
    /// <c>BASE64URL(SHA256(ASCII(codeVerifier)))</c> without padding.</summary>
    public static string ComputeCodeChallenge(string codeVerifier)
    {
        ArgumentException.ThrowIfNullOrEmpty(codeVerifier);
        return Base64UrlEncode(SHA256.HashData(Encoding.ASCII.GetBytes(codeVerifier)));
    }

    /// <summary>Lower-case hex SHA-256 of <paramref name="state"/>, for use in a store key
    /// (<c>live:google:oauth:{hash}</c>) so the raw <c>state</c> value is never persisted.</summary>
    public static string HashState(string state)
    {
        ArgumentException.ThrowIfNullOrEmpty(state);
        return Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(state)));
    }

    internal static string Base64UrlEncode(ReadOnlySpan<byte> bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');
}
