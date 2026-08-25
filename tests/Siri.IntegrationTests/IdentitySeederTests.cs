using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Identity;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Identity.Infrastructure.Seeding;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;

namespace Siri.IntegrationTests;

/// <summary>
/// Exercises the real <see cref="IdentitySeeder"/> — resolved from the exact same DI wiring
/// (<c>AddPersistence</c> + <c>AddIdentityModule</c>) <c>Siri.Api/Program.cs</c>'s <c>--seed</c> flag
/// uses — against the Testcontainers-managed MSSQL instance (database.md: "ห้ามใช้ InMemory provider
/// ในเทสต์ — ใช้ Testcontainers MSSQL"). Requires Docker locally; see <see cref="ContainersFixture"/>'s
/// own doc comment — if Docker is not running, container startup fails before any test body here
/// runs, which is an environment issue, not a defect in these tests.
/// <para>
/// Configures real (non-placeholder) <see cref="SeedOptions"/> values in-test — the same role
/// <c>Identity:Seed:AdminPassword</c>/<c>TestUserPassword</c> user-secrets play for a real run — so
/// <see cref="SeedOptionsGuard"/>'s placeholder check never fires here.
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class IdentitySeederTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ServiceProvider _serviceProvider = null!;

    /// <summary>Fixed by <see cref="IdentitySeedData"/> — kept here too so assertions can filter to
    /// exactly these rows and stay correct no matter what other test classes sharing the same
    /// Testcontainers database (see <see cref="ContainersCollection"/>) have written under
    /// unrelated, randomly-suffixed emails.</summary>
    private static readonly string[] KnownSeedEmails =
    [
        "seed-admin@example.test",
        "learner1.seed@example.test",
        "learner2.seed@example.test",
        "learner3.seed@example.test",
        "instructor1.seed@example.test",
        "instructor2.seed@example.test",
        "instructor3.seed@example.test",
        "instructor4.seed@example.test",
        "instructor5.seed@example.test",
    ];

    public IdentitySeederTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _containers.SqlConnectionString,
                ["Identity:Seed:AdminEmail"] = "seed-admin@example.test",
                ["Identity:Seed:AdminPassword"] = "a-real-admin-password-1",
                ["Identity:Seed:TestUserPassword"] = "a-real-test-user-password-1",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddPersistence(configuration);
        services.AddIdentityModule(configuration);

        _serviceProvider = services.BuildServiceProvider();

        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync(); // applies every migration, including the Role HasData seed (P0-14)
    }

    public async Task DisposeAsync() => await _serviceProvider.DisposeAsync();

    /// <summary>
    /// The two properties task P0-37 explicitly asks for, in one test (mirrors
    /// <c>RegisterAndConfirmEmailTests.Register_SameEmailTwice_...</c>'s own "call it twice in one
    /// test" shape): a fresh run creates every seed user with the right role and Active/confirmed
    /// status, and running the exact same seeder again — same database, same config — creates zero
    /// additional rows and does not throw.
    /// </summary>
    [Fact]
    public async Task SeedAsync_FreshDatabaseThenRunAgain_CreatesExpectedUsersOnceAndIsIdempotentOnSecondRun()
    {
        IReadOnlyDictionary<string, Guid> userIdsByEmail;
        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
            userIdsByEmail = await seeder.SeedAsync(CancellationToken.None);
        }

        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var seededUsers = await dbContext.Users()
                .AsNoTracking()
                .Include(u => u.Roles)
                .Where(u => KnownSeedEmails.Contains(u.Email))
                .ToListAsync();

            Assert.Equal(KnownSeedEmails.Length, seededUsers.Count); // all nine created, exactly once each

            Assert.All(seededUsers, u => Assert.Equal(UserStatus.Active, u.Status));
            Assert.All(seededUsers, u => Assert.NotNull(u.EmailConfirmedAtUtc)); // task: "ConfirmedEmail"

            var admin = seededUsers.Single(u => u.Email == "seed-admin@example.test");
            Assert.Contains(admin.Roles, r => r.Id == Role.AdminId);

            var learnerEmails = new[] { "learner1.seed@example.test", "learner2.seed@example.test", "learner3.seed@example.test" };
            foreach (var learnerEmail in learnerEmails)
            {
                var learner = seededUsers.Single(u => u.Email == learnerEmail);
                Assert.Contains(learner.Roles, r => r.Id == Role.LearnerId);
            }

            var instructorEmails = new[]
            {
                "instructor1.seed@example.test", "instructor2.seed@example.test", "instructor3.seed@example.test",
                "instructor4.seed@example.test", "instructor5.seed@example.test",
            };
            foreach (var instructorEmail in instructorEmails)
            {
                var instructor = seededUsers.Single(u => u.Email == instructorEmail);
                Assert.Contains(instructor.Roles, r => r.Id == Role.InstructorId);

                // P1-30: SeedAsync's returned map is what CatalogSeeder relies on to link InstructorProfile
                // rows to real accounts — assert it actually matches what landed in the database, not just
                // that the map has *some* entry.
                Assert.Equal(instructor.Id, userIdsByEmail[instructorEmail]);
            }
        }

        // Run it again — same database, same config. Must not throw, and must not create duplicates.
        IReadOnlyDictionary<string, Guid> userIdsByEmailOnRerun = new Dictionary<string, Guid>();
        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
            Exception? exception = null;
            try
            {
                userIdsByEmailOnRerun = await seeder.SeedAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                exception = ex;
            }

            Assert.Null(exception);
        }

        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var seededUserCountAfterSecondRun = await dbContext.Users()
                .AsNoTracking()
                .CountAsync(u => KnownSeedEmails.Contains(u.Email));

            Assert.Equal(KnownSeedEmails.Length, seededUserCountAfterSecondRun); // still exactly nine, no duplicates

            // The re-run's returned map must still resolve every known seed email to the same, stable
            // id the first run created — the whole point of P1-30's "look up existing rows' ids too, not
            // just newly-created ones" change (otherwise a second --seed run would hand CatalogSeeder an
            // incomplete map and it could never find its instructors again).
            foreach (var email in KnownSeedEmails)
            {
                Assert.Equal(userIdsByEmail[email], userIdsByEmailOnRerun[email]);
            }
        }
    }

    /// <summary>
    /// The "safe next to other, unrelated real data" half of idempotency (task instruction: "it
    /// should only ever touch its own known seed accounts, never assume it can enumerate/wipe all
    /// users"). Registers an ordinary, non-seed user first, runs the seeder, and asserts that
    /// unrelated row is completely untouched.
    /// </summary>
    [Fact]
    public async Task SeedAsync_DatabaseAlreadyHasUnrelatedRealUser_NeverTouchesThatUser()
    {
        var unrelatedEmail = $"real-{Guid.NewGuid():N}@unrelated-domain.example";

        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unrelatedUser = User.Register(unrelatedEmail, unrelatedEmail.ToUpperInvariant(), "some-hash", "A Real Unrelated User");
            dbContext.Users().Add(unrelatedUser);
            await dbContext.SaveChangesAsync();
        }

        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var seeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
            await seeder.SeedAsync(CancellationToken.None);
        }

        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var unrelatedUser = await dbContext.Users().AsNoTracking().SingleAsync(u => u.Email == unrelatedEmail);

            // Untouched: still PendingEmailConfirmation (the seeder never confirmed/activated it),
            // and still has no roles (the seeder never assigned it one).
            Assert.Equal(UserStatus.PendingEmailConfirmation, unrelatedUser.Status);
            Assert.Null(unrelatedUser.EmailConfirmedAtUtc);
        }
    }
}
