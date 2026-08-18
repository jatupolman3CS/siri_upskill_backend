using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using Siri.Modules.Identity.Domain;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>
/// <see cref="IUserPasswordHasher"/> implementation backed by ASP.NET Core Identity's
/// <see cref="PasswordHasher{TUser}"/> (PBKDF2, per SECURITY.md — "ASP.NET Core Identity hasher
/// (PBKDF2 ≥ 600k iterations)"). Deliberately not a hand-rolled hashing scheme
/// (.claude/rules/security.md forbids adding new crypto dependencies/logic without asking; this
/// uses the framework's own well-tested implementation instead of writing one).
/// <para>
/// <see cref="PasswordHasher{TUser}"/> defaults to 100,000 iterations if left unconfigured, which
/// is below SECURITY.md's explicit "≥ 600k iterations" floor — <see cref="IterationCount"/> makes
/// that requirement explicit rather than relying on a framework default that could silently change
/// or fall short. The iteration count is embedded in every hash's own format marker (V3), so
/// raising it here does not invalidate already-issued hashes; they keep verifying at whatever
/// count they were created with, and only newly-hashed passwords use the new value.
/// </para>
/// <see cref="PasswordHasher{TUser}"/> is stateless and thread-safe, so this is safe to register
/// as a singleton.
/// </summary>
public sealed class UserPasswordHasher : IUserPasswordHasher
{
    public const int IterationCount = 600_000;

    private readonly PasswordHasher<User> _inner =
        new(Options.Create(new PasswordHasherOptions { IterationCount = IterationCount }));

    public string HashPassword(User user, string password)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(password);

        return _inner.HashPassword(user, password);
    }

    public PasswordVerificationResult VerifyPassword(User user, string hashedPassword, string providedPassword)
    {
        ArgumentNullException.ThrowIfNull(user);
        ArgumentException.ThrowIfNullOrWhiteSpace(hashedPassword);
        ArgumentException.ThrowIfNullOrWhiteSpace(providedPassword);

        return _inner.VerifyHashedPassword(user, hashedPassword, providedPassword);
    }
}
