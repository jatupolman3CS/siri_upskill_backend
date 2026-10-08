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
/// <para>
/// <b>External services mode (opt-in, for machines without Docker).</b> When
/// <see cref="ExternalTestServices.PostgresAdminConnectionEnvVar"/> and
/// <see cref="ExternalTestServices.RedisEnvVar"/> are both set, no container is created: a throwaway
/// <c>siri_it_&lt;random&gt;</c> database (owned by a throwaway non-superuser role) is created on the already
/// running local PostgreSQL, the Redis connection string points at a dedicated local database index, and both
/// are cleaned up on dispose. See <see cref="ExternalTestServices"/> for the safety rules (loopback only).
/// Unset = unchanged Testcontainers behaviour.
/// </para>
/// </summary>
public sealed class ContainersFixture : IAsyncLifetime
{
    // Image goes through the constructor, not .WithImage(): the parameterless PostgreSqlBuilder()
    // is [Obsolete] in Testcontainers 4.14.0 and this repo builds with TreatWarningsAsErrors.
    // Same shape the MsSqlBuilder call here used before P0-41.
    private readonly PostgreSqlContainer? _sqlContainer;
    private readonly RedisContainer? _redisContainer;

    private readonly bool _external;
    private ExternalTestServices.ExternalPostgresDatabase? _externalDatabase;
    private StackExchange.Redis.ConfigurationOptions? _externalRedis;
    private string? _externalRedisConnectionString;

    public ContainersFixture()
    {
        var postgresRequested = ExternalTestServices.PostgresRequested;
        var redisRequested = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ExternalTestServices.RedisEnvVar));
        if (postgresRequested != redisRequested)
        {
            throw new InvalidOperationException(
                $"External test services mode needs both {ExternalTestServices.PostgresAdminConnectionEnvVar} and " +
                $"{ExternalTestServices.RedisEnvVar}; only one is set.");
        }

        _external = postgresRequested;
        if (_external)
        {
            return;
        }

        _sqlContainer = new PostgreSqlBuilder("postgres:17-alpine").Build();
        _redisContainer = new RedisBuilder("redis:7-alpine").Build();
    }

    /// <summary>True when running against already-running local services instead of Testcontainers.</summary>
    public bool IsExternal => _external;

    /// <summary>Npgsql connection string for the throwaway PostgreSQL container (or the throwaway local database in
    /// external mode). Name kept from the SQL Server era on purpose: 35 test files read it, and renaming would
    /// bury the P0-41 diff in mechanical churn without telling a reader anything the type doesn't already say.</summary>
    public string SqlConnectionString => _external
        ? _externalDatabase?.ConnectionString ?? throw new InvalidOperationException("ContainersFixture is not initialized.")
        : _sqlContainer!.GetConnectionString();

    public string RedisConnectionString => _external
        ? _externalRedisConnectionString ?? throw new InvalidOperationException("ContainersFixture is not initialized.")
        : _redisContainer!.GetConnectionString();

    public async Task InitializeAsync()
    {
        if (_external)
        {
            // Validate (loopback-only, dedicated redis db) BEFORE any connection is opened.
            _externalRedis = ExternalTestServices.ParseRedis(
                Environment.GetEnvironmentVariable(ExternalTestServices.RedisEnvVar));
            _externalRedisConnectionString = Environment.GetEnvironmentVariable(ExternalTestServices.RedisEnvVar);

            _externalDatabase = await ExternalTestServices.CreatePostgresDatabaseAsync();
            // Start from a clean dedicated database even if an earlier run was killed before its cleanup.
            await ExternalTestServices.FlushDedicatedRedisDatabaseAsync(_externalRedis);
            return;
        }

        await Task.WhenAll(
            _sqlContainer!.StartAsync(),
            _redisContainer!.StartAsync());
    }

    public async Task DisposeAsync()
    {
        if (_external)
        {
            if (_externalRedis is not null)
            {
                try
                {
                    await ExternalTestServices.FlushDedicatedRedisDatabaseAsync(_externalRedis);
                }
                catch (StackExchange.Redis.RedisException exception)
                {
                    Console.Error.WriteLine($"[ExternalTestServices] could not flush the dedicated redis database: {exception.GetType().Name}");
                }
            }

            if (_externalDatabase is not null)
            {
                await _externalDatabase.DisposeAsync();
            }

            return;
        }

        await Task.WhenAll(
            _sqlContainer!.DisposeAsync().AsTask(),
            _redisContainer!.DisposeAsync().AsTask());
    }
}

[CollectionDefinition(Name)]
public sealed class ContainersCollection : ICollectionFixture<ContainersFixture>
{
    public const string Name = "Containers";
}
