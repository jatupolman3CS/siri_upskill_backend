using System.Security.Cryptography;
using System.Text;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>
/// <see cref="ISecurityTokenGenerator"/> implementation: <see cref="RandomNumberGenerator"/> for the
/// raw value (a real CSPRNG, not <see cref="Random"/> — that is the whole reason this token's hash
/// can safely use a fast, unsalted algorithm, see <c>Domain/UserSecurityToken.cs</c>'s doc comment)
/// and SHA-256 for the hash stored at rest. Stateless (no fields), so safe to register as a
/// singleton — same lifetime story as <see cref="UserPasswordHasher"/>.
/// </summary>
public sealed class SecurityTokenGenerator : ISecurityTokenGenerator
{
    /// <summary>256 bits of entropy — the raw token is never guessable by brute force within any
    /// realistic rate-limited attack window (the "auth" rate-limit policy caps guesses at 5/minute
    /// per Program.cs, on top of this).</summary>
    private const int RawTokenByteLength = 32;

    public (string RawToken, string TokenHash) Generate()
    {
        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(RawTokenByteLength));
        return (rawToken, Hash(rawToken));
    }

    public string Hash(string rawToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rawToken);

        // Convert.ToHexString outputs uppercase hex; the raw token above is already produced by this
        // same call, but Hash also has to accept whatever a caller submits verbatim (ConfirmEmail's
        // raw token comes back from an email link) — normalizing case here would silently accept
        // case-mangled tokens as valid, which is not what "compare the hash exactly" should mean, so
        // deliberately no ToUpper/ToLower normalization: the submitted value is hashed byte-for-byte
        // as UTF-8 text, exactly as generated.
        var hashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(rawToken));
        return Convert.ToHexString(hashBytes);
    }
}
