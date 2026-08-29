using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests.TestData;

/// <summary>
/// Fluent builder for persisted test users (task P0-20's data-builder piece). Wraps the exact
/// seeding steps the earlier integration tests each hand-rolled (<c>LoginAndRefreshTests</c>'
/// <c>CreateActiveUserAsync</c> and friends): hash a known password with the real
/// <see cref="IUserPasswordHasher"/>, <see cref="USER.Register"/>, optionally
/// <see cref="USER.ConfirmEmail"/>, optionally <see cref="USER.AssignRole"/> against the
/// migration-seeded roles, then save — so new tests state only what they care about and inherit
/// safe defaults for the rest.
/// <para>
/// Defaults: unique <c>@example.test</c> email per builder instance (never collides across tests
/// sharing the collection's database, never a real address), a fixed known password exposed via
/// <see cref="Password"/> so the test can log in as the user afterwards, confirmed email (most
/// tests want a login-capable user), no roles (matching real registration — <c>RegisterHandler</c>
/// assigns none).
/// </para>
/// <para>
/// Pass a <b>scoped</b> provider (create one scope per test, same as every existing test does) —
/// the builder resolves <see cref="AppDbContext"/> from it, so the returned <see cref="USER"/> is
/// tracked by that scope's context and can be reloaded/asserted through it directly.
/// </para>
/// </summary>
public sealed class TestUserBuilder
{
    private string _email = $"user-{Guid.NewGuid():N}@example.test";
    private string _password = "Correct-Horse-Battery-Staple-9";
    private string _displayName = "Test USER";
    private bool _confirmEmail = true;
    private readonly List<string> _roleNames = [];

    /// <summary>The email the built user will have — readable before/after <see cref="BuildAsync"/>
    /// so HTTP-level tests can log in without re-deriving it.</summary>
    public string Email => _email;

    /// <summary>The plaintext password the built user's hash was derived from.</summary>
    public string Password => _password;

    public TestUserBuilder WithEmail(string email)
    {
        _email = email;
        return this;
    }

    public TestUserBuilder WithPassword(string password)
    {
        _password = password;
        return this;
    }

    public TestUserBuilder WithDisplayName(string displayName)
    {
        _displayName = displayName;
        return this;
    }

    /// <summary>Leave the account pending email confirmation (login must reject it).</summary>
    public TestUserBuilder WithUnconfirmedEmail()
    {
        _confirmEmail = false;
        return this;
    }

    /// <summary>Assign one of the migration-seeded system roles by exact name — use the
    /// <see cref="ROLE"/> constants (<see cref="ROLE.InstructorName"/> etc.), not string literals.</summary>
    public TestUserBuilder WithRole(string roleName)
    {
        _roleNames.Add(roleName);
        return this;
    }

    public async Task<USER> BuildAsync(IServiceProvider scopedServices, CancellationToken cancellationToken = default)
    {
        var dbContext = scopedServices.GetRequiredService<AppDbContext>();
        var passwordHasher = scopedServices.GetRequiredService<IUserPasswordHasher>();
        var clock = scopedServices.GetRequiredService<IClock>();

        // Same two-step Register as LoginAndRefreshTests.CreateActiveUserAsync: the hasher needs a
        // USER instance, but the hash must be baked into the persisted registration.
        var normalizedEmail = _email.ToUpperInvariant();
        var throwaway = USER.Register(_email, normalizedEmail, "placeholder", _displayName);
        var passwordHash = passwordHasher.HashPassword(throwaway, _password);
        var user = USER.Register(_email, normalizedEmail, passwordHash, _displayName);

        if (_confirmEmail)
        {
            user.ConfirmEmail(clock);
        }

        foreach (var roleName in _roleNames)
        {
            var role = await dbContext.Roles().SingleAsync(r => r.Name == roleName, cancellationToken);
            user.AssignRole(role);
        }

        dbContext.Users().Add(user);
        await dbContext.SaveChangesAsync(cancellationToken);

        return user;
    }
}
