using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Siri.Api.Configuration;
using Xunit;

namespace Siri.UnitTests.Security;

public sealed class HeartbeatRateLimiterPolicyTests
{
    private static DefaultHttpContext CreateHttpContext(string? userId, string? ip = null)
    {
        var context = new DefaultHttpContext();
        if (userId is not null)
        {
            var identity = new ClaimsIdentity(
                [new Claim(ClaimTypes.NameIdentifier, userId)],
                "TestAuth");
            context.User = new ClaimsPrincipal(identity);
        }

        if (ip is not null)
        {
            context.Connection.RemoteIpAddress = System.Net.IPAddress.Parse(ip);
        }

        return context;
    }

    [Fact]
    public void CreateHeartbeatPartition_ExtractsNameIdentifierClaim()
    {
        var context = CreateHttpContext("usr-alice-123");

        var partition = RateLimiterConfiguration.CreateHeartbeatPartition(context);

        Assert.Equal("usr-alice-123", partition.PartitionKey);
    }

    [Fact]
    public void CreateHeartbeatPartition_ExtractsSubClaimWhenNameIdentifierMissing()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("sub", "usr-bob-456")], "TestAuth"));

        var partition = RateLimiterConfiguration.CreateHeartbeatPartition(context);

        Assert.Equal("usr-bob-456", partition.PartitionKey);
    }

    [Fact]
    public void CreateHeartbeatPartition_FallsBackToRemoteIpWhenNoUser()
    {
        var context = CreateHttpContext(null, "192.168.1.50");

        var partition = RateLimiterConfiguration.CreateHeartbeatPartition(context);

        Assert.Equal("192.168.1.50", partition.PartitionKey);
    }

    [Fact]
    public void CreateHeartbeatPartition_FallsBackToAnonymousWhenNoUserAndNoIp()
    {
        var context = new DefaultHttpContext();

        var partition = RateLimiterConfiguration.CreateHeartbeatPartition(context);

        Assert.Equal("anonymous", partition.PartitionKey);
    }

    [Fact]
    public void HeartbeatLimiter_Allows6RequestsPer30Seconds_RejectsSeventh()
    {
        var context = CreateHttpContext("usr-test-limiter");

        var partition = RateLimiterConfiguration.CreateHeartbeatPartition(context);
        var limiter = partition.Factory(partition.PartitionKey);

        // First 6 requests should be allowed
        for (var i = 1; i <= 6; i++)
        {
            using var lease = limiter.AttemptAcquire(1);
            Assert.True(lease.IsAcquired, $"Request {i} should acquire lease");
        }

        // 7th request within 30s window must be rejected (429)
        using var rejected = limiter.AttemptAcquire(1);
        Assert.False(rejected.IsAcquired, "Request 7 within 30s window must be rejected (429)");
    }

    [Fact]
    public void HeartbeatLimiter_DifferentUsersGetDistinctLimiters()
    {
        var context1 = CreateHttpContext("usr-1");
        var context2 = CreateHttpContext("usr-2");

        var partition1 = RateLimiterConfiguration.CreateHeartbeatPartition(context1);
        var partition2 = RateLimiterConfiguration.CreateHeartbeatPartition(context2);

        var limiter1 = partition1.Factory(partition1.PartitionKey);
        var limiter2 = partition2.Factory(partition2.PartitionKey);

        // Exhaust user 1's quota
        for (var i = 1; i <= 6; i++)
        {
            using var lease = limiter1.AttemptAcquire(1);
            Assert.True(lease.IsAcquired);
        }
        using var rejected1 = limiter1.AttemptAcquire(1);
        Assert.False(rejected1.IsAcquired);

        // User 2 still has full quota available
        for (var i = 1; i <= 6; i++)
        {
            using var lease = limiter2.AttemptAcquire(1);
            Assert.True(lease.IsAcquired, $"User 2 request {i} should be granted despite user 1 exhaustion");
        }
        using var rejected2 = limiter2.AttemptAcquire(1);
        Assert.False(rejected2.IsAcquired);
    }
}
