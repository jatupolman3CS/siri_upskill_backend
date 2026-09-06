using System.Data;
using Npgsql;
using Siri.IntegrationTests.Fixtures;
using StackExchange.Redis;

namespace Siri.IntegrationTests;

/// <summary>
/// Confirms the Testcontainers-managed PostgreSQL and Redis containers actually come up and accept
/// connections. Requires Docker running locally; if Docker is unavailable, container startup in
/// <see cref="ContainersFixture"/> fails before this test body even runs — an environment issue,
/// not a defect in this scaffold.
/// </summary>
[Collection(ContainersCollection.Name)]
public class ContainersSmokeTests(ContainersFixture containers)
{
    [Fact]
    public async Task Containers_PostgresAndRedis_AreReachable()
    {
        await using var sqlConnection = new NpgsqlConnection(containers.SqlConnectionString);
        await sqlConnection.OpenAsync();
        Assert.Equal(ConnectionState.Open, sqlConnection.State);

        await using var redis = await ConnectionMultiplexer.ConnectAsync(containers.RedisConnectionString);
        Assert.True(redis.IsConnected);

        var latency = await redis.GetDatabase().PingAsync();
        Assert.True(latency >= TimeSpan.Zero);
    }
}
