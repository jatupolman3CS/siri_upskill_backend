using Microsoft.AspNetCore.Identity;
using Siri.Modules.Identity.Domain;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>
/// Hashes and verifies a <see cref="User"/>'s password. Nothing calls this yet in P0-14 — real
/// registration (P0-15) and login (P0-16) wire it up. Kept as a narrow interface (not a god
/// service) per backend.md, and injectable so handlers never hash/verify by hand.
/// </summary>
public interface IUserPasswordHasher
{
    /// <summary>Hashes <paramref name="password"/> for storage in <see cref="User.PasswordHash"/>.</summary>
    string HashPassword(User user, string password);

    /// <summary>Verifies <paramref name="providedPassword"/> against a previously hashed value.</summary>
    PasswordVerificationResult VerifyPassword(User user, string hashedPassword, string providedPassword);
}
