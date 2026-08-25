using Siri.Modules.Identity.Domain;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>
/// Generates the raw, one-time value handed to a user (in a confirmation link, later a
/// password-reset link) and hashes it for storage in <see cref="UserSecurityToken.TokenHash"/>. Kept
/// as a narrow, injectable interface — same reasoning as <see cref="IUserPasswordHasher"/> — so
/// handlers never touch <see cref="System.Security.Cryptography"/> directly, and so a raw token is
/// hashed identically at issuance (Register) and at redemption (ConfirmEmail) time.
/// </summary>
public interface ISecurityTokenGenerator
{
    /// <summary>Generates a new cryptographically random raw token and its hash together, so a
    /// caller can never accidentally persist the raw value or hash something else by mistake.</summary>
    (string RawToken, string TokenHash) Generate();

    /// <summary>Hashes an already-generated raw token — used at redemption time to compute the same
    /// hash from the value a user submits, for an exact-match lookup against <see cref="UserSecurityToken.TokenHash"/>.</summary>
    string Hash(string rawToken);
}
