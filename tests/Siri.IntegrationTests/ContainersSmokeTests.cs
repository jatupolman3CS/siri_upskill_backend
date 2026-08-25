using System.Data;
using Microsoft.Data.SqlClient;
using Siri.IntegrationTests.Fixtures;
using StackExchange.Redis;

namespace Siri.IntegrationTests;

/// <summary>
/// Confirms the Testcontainers-managed MSSQL and Redis containers actually come up and accept
/// connections. Requires Docker running locally; if Docker is unavailable, container startup in
/// <see cref="ContainersFixture"/> fails before this test body even runs — an environment issue,
/// not a defect in this scaffold.
/// </summary>
[Collection(ContainersCollection.Name)]
public class ContainersSmokeTests(ContainersFixture containers)
{
    [Fact]
    public async Task Containers_SqlServerAndRedis_AreReachable()
    {
        await using var sqlConnection = new SqlConnection(containers.SqlConnectionString);
        await sqlConnection.OpenAsync();
        Assert.Equal(ConnectionState.Open, sqlConnection.State);

        await using var redis = await ConnectionMultiplexer.ConnectAsync(containers.RedisConnectionString);
        Assert.True(redis.IsConnected);

        var latency = await redis.GetDatabase().PingAsync();
        Assert.True(latency >= TimeSpan.Zero);
    }
}
