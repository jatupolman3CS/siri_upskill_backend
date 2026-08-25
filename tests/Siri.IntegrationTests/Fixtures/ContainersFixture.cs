using Testcontainers.MsSql;
using Testcontainers.Redis;

namespace Siri.IntegrationTests.Fixtures;

/// <summary>
/// Spins up a real, throwaway MSSQL container and a real Redis container for the duration of the
/// test collection (database.md: "ห้ามใช้ InMemory provider ในเทสต์ — ใช้ Testcontainers MSSQL").
/// Requires Docker to be running locally; if it is not, container startup fails here rather than
/// mid-test, and the failure is a Docker/environment issue, not a scaffold defect.
/// </summary>
public sealed class ContainersFixture : IAsyncLifetime
{
    private readonly MsSqlContainer _sqlContainer =
        new MsSqlBuilder("mcr.microsoft.com/mssql/server:2022-latest").Build();

    private readonly RedisContainer _redisContainer =
        new RedisBuilder("redis:7-alpine").Build();

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
