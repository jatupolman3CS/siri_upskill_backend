using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace Siri.IntegrationTests.Fixtures;

/// <summary>
/// Spins up a real, throwaway PostgreSQL container and a real Redis container for the duration of the
/// test collection (database.md: "ห้ามใช้ InMemory provider ในเทสต์ — ใช้ Testcontainers").
/// Requires Docker to be running locally; if it is not, container startup fails here rather than
/// mid-test, and the failure is a Docker/environment issue, not a scaffold defect.
/// <para>
/// The image is pinned to the same major version production runs (task P0-41). Two deliberate
/// differences from production, neither of which affects correctness of the code under test:
/// this cluster uses the image's default locale rather than production's ICU <c>th-TH</c>, so only
/// the collation order of Thai text differs (never which rows match); and the container's role is a
/// superuser, where production's <c>siriupskill_app</c> owns just its own database — the migration's
/// <c>CREATE EXTENSION pg_trgm</c> works either way, since pg_trgm is a trusted extension a database
/// owner may create.
/// </para>
/// </summary>
public sealed class ContainersFixture : IAsyncLifetime
{
    // Image goes through the constructor, not .WithImage(): the parameterless PostgreSqlBuilder()
    // is [Obsolete] in Testcontainers 4.14.0 and this repo builds with TreatWarningsAsErrors.
    // Same shape the MsSqlBuilder call here used before P0-41.
    private readonly PostgreSqlContainer _sqlContainer =
        new PostgreSqlBuilder("postgres:17-alpine").Build();

    private readonly RedisContainer _redisContainer =
        new RedisBuilder("redis:7-alpine").Build();

    /// <summary>Npgsql connection string for the throwaway PostgreSQL container. Name kept from the
    /// SQL Server era on purpose: 35 test files read it, and renaming would bury the P0-41 diff in
    /// mechanical churn without telling a reader anything the type doesn't already say.</summary>
    public string SqlConnectionString => _sqlContainer.GetConnectionString();

    public string RedisConnectionString => _redisContainer.GetConnectionString();

    public async Task InitializeAsync()
    {
        await Task.WhenAll(
            _sqlContainer.StartAsync(),
            _redisContainer.StartAsync());
    }

    public async Task DisposeAsync()
    {
        await Task.WhenAll(
            _sqlContainer.DisposeAsync().AsTask(),
            _redisContainer.DisposeAsync().AsTask());
    }
}

[CollectionDefinition(Name)]
public sealed class ContainersCollection : ICollectionFixture<ContainersFixture>
{
    public const string Name = "Containers";
}
