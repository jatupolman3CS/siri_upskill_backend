using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Catalog;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Catalog.Infrastructure.Seeding;
using Siri.Modules.Identity;
using Siri.Modules.Identity.Infrastructure.Seeding;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;

namespace Siri.IntegrationTests;

/// <summary>
/// Exercises the real <see cref="CatalogSeeder"/> — resolved from the same minimal DI wiring
/// <see cref="IdentitySeederTests"/> already uses for <c>IdentitySeeder</c> (raw <see cref="ServiceCollection"/>,
/// no <c>WebApplication</c> needed — seeders have no HTTP surface) — against the Testcontainers-managed
/// MSSQL instance (database.md: "ห้ามใช้ InMemory provider ในเทสต์ — ใช้ Testcontainers MSSQL"). Requires
/// Docker locally; see <see cref="ContainersFixture"/>'s own doc comment.
/// <para>
/// Always seeds Identity first, exactly like <c>Siri.Api/Program.cs</c>'s <c>--seed</c> flag does — this
/// class exists to prove that composition works end-to-end, not just that <see cref="CatalogSeeder"/> in
/// isolation is internally consistent (already covered, DB-free, by <c>CatalogSeedDataTests</c>).
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class CatalogSeederTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ServiceProvider _serviceProvider = null!;

    public CatalogSeederTests(ContainersFixture containers)
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
        services.AddCatalogModule(configuration);

        _serviceProvider = services.BuildServiceProvider();

        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync(); // applies every migration, including AddInstructorProfiles/AddCourseDomain/AddCategories
    }

    public async Task DisposeAsync() => await _serviceProvider.DisposeAsync();

    private async Task<IReadOnlyDictionary<string, Guid>> SeedIdentityAsync()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var identitySeeder = scope.ServiceProvider.GetRequiredService<IdentitySeeder>();
        return await identitySeeder.SeedAsync(CancellationToken.None);
    }

    /// <summary>
    /// The task's core requirements in one test (mirrors <c>IdentitySeederTests</c>'s own "call it twice
    /// in one test" shape): a fresh run creates exactly 3 categories / 5 approved instructor profiles /
    /// 20 published courses, every cross-reference (category, instructor, section/episode/outcome/
    /// requirement) lands correctly, and running the exact same seeder again — same database, same
    /// instructor id map — creates zero additional rows and does not throw.
    /// </summary>
    [Fact]
    public async Task SeedAsync_FreshDatabaseThenRunAgain_CreatesExpectedCatalogDataOnceAndIsIdempotentOnSecondRun()
    {
        var userIdsByEmail = await SeedIdentityAsync();

        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var catalogSeeder = scope.ServiceProvider.GetRequiredService<CatalogSeeder>();
            await catalogSeeder.SeedAsync(userIdsByEmail, CancellationToken.None);
        }

        var expectedCategorySlugs = CatalogSeedData.BuildCategories().Select(c => c.Slug).ToHashSet();
        var expectedInstructorEmails = CatalogSeedData.BuildInstructors().Select(i => i.IdentityEmail).ToHashSet();
        var expectedCourseTitles = CatalogSeedData.BuildCourses().Select(c => c.Title).ToHashSet();

        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var categories = await dbContext.Categories()
                .AsNoTracking()
                .Where(c => expectedCategorySlugs.Contains(c.Slug))
                .ToListAsync();
            Assert.Equal(3, categories.Count);
            Assert.All(categories, c => Assert.Null(c.ParentId)); // all 3 are root categories
            Assert.All(categories, c => Assert.True(c.IsActive));

            var expectedInstructorUserIds = expectedInstructorEmails.Select(email => userIdsByEmail[email]).ToHashSet();
            var instructorProfiles = await dbContext.InstructorProfiles()
                .AsNoTracking()
                .Where(p => expectedInstructorUserIds.Contains(p.UserId))
                .ToListAsync();
            Assert.Equal(5, instructorProfiles.Count);
            Assert.All(instructorProfiles, p => Assert.Equal(InstructorApplicationStatus.Approved, p.Status));
            Assert.All(instructorProfiles, p => Assert.NotNull(p.ApprovedAtUtc));

            var courses = await dbContext.Courses()
                .AsNoTracking()
                .Include(c => c.Sections).ThenInclude(s => s.Episodes)
                .Include(c => c.Outcomes)
                .Include(c => c.Requirements)
                .Where(c => expectedCourseTitles.Contains(c.Title))
                .ToListAsync();
            Assert.Equal(20, courses.Count);

            var categoryIds = categories.Select(c => c.Id).ToHashSet();
            var instructorProfileIds = instructorProfiles.Select(p => p.Id).ToHashSet();

            Assert.All(courses, c => Assert.Equal(CourseStatus.Published, c.Status));
            Assert.All(courses, c => Assert.NotNull(c.PublishedAtUtc));
            Assert.All(courses, c => Assert.Contains(c.CategoryId, categoryIds)); // every course's category is one of the 3 seeded ones
            Assert.All(courses, c => Assert.Contains(c.InstructorId, instructorProfileIds)); // every course's instructor is one of the 5 seeded ones
            Assert.All(courses, c => Assert.NotEmpty(c.Sections));
            Assert.All(courses, c => Assert.All(c.Sections, s => Assert.NotEmpty(s.Episodes)));
            Assert.All(courses, c => Assert.All(c.Sections.SelectMany(s => s.Episodes), e => Assert.NotNull(e.MediaAssetId)));
            Assert.All(courses, c => Assert.Contains(c.Sections.SelectMany(s => s.Episodes), e => e.IsFreePreview)); // exactly the first episode, but "at least one" is what Publish/UI care about
            Assert.All(courses, c => Assert.NotEmpty(c.Outcomes));
            Assert.All(courses, c => Assert.NotEmpty(c.Requirements));
            Assert.All(courses, c => Assert.True(c.ComparePrice > c.Price));
        }

        // Run it again — same database, same instructor id map. Must not throw, and must not create duplicates.
        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var catalogSeeder = scope.ServiceProvider.GetRequiredService<CatalogSeeder>();
            var exception = await Record.ExceptionAsync(() => catalogSeeder.SeedAsync(userIdsByEmail, CancellationToken.None));
            Assert.Null(exception);
        }

        await using (var scope = _serviceProvider.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            var categoryCount = await dbContext.Categories().AsNoTracking().CountAsync(c => expectedCategorySlugs.Contains(c.Slug));
            var courseCount = await dbContext.Courses().AsNoTracking().CountAsync(c => expectedCourseTitles.Contains(c.Title));

            Assert.Equal(3, categoryCount); // still exactly 3, no duplicates
            Assert.Equal(20, courseCount); // still exactly 20, no duplicates
        }
    }

    /// <summary>
    /// The guard <c>CatalogSeeder.ResolveInstructorUserId</c> exists for — if <c>IdentitySeeder</c> ever
    /// drifted out of sync with <c>CatalogSeedData</c> (an instructor email <see cref="CatalogSeedData"/>
    /// references that the caller's id map does not have), this must fail loudly with a message pointing
    /// at the actual cause, not a generic <c>KeyNotFoundException</c> or a silently-skipped instructor.
    /// </summary>
    [Fact]
    public async Task SeedAsync_IncompleteInstructorUserIdMap_ThrowsDescriptiveException()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var catalogSeeder = scope.ServiceProvider.GetRequiredService<CatalogSeeder>();

        var exception = await Record.ExceptionAsync(
            () => catalogSeeder.SeedAsync(new Dictionary<string, Guid>(), CancellationToken.None));

        var invalidOperationException = Assert.IsType<InvalidOperationException>(exception);
        Assert.Contains("instructor1.seed@example.test", invalidOperationException.Message, StringComparison.Ordinal);
    }
}
