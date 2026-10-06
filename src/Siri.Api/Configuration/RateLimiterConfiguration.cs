using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;

namespace Siri.Api.Configuration;

/// <summary>
/// Rate limiting policies and partitioners for <see cref="Program"/>.
/// </summary>
public static class RateLimiterConfiguration
{
    public const string HeartbeatPolicyName = "heartbeat";

    /// <summary>
    /// Partitioned fixed-window limiter for heartbeat requests (X-29).
    /// Quota is 6 requests per 30 seconds per authenticated user, preventing noisy-neighbor
    /// DOS across concurrent learners.
    /// </summary>
    public static RateLimitPartition<string> CreateHeartbeatPartition(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var userId = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return RateLimitPartition.GetFixedWindowLimiter(
            userId,
            _ => new FixedWindowRateLimiterOptions
            {
                Window = TimeSpan.FromSeconds(30),
                PermitLimit = 6,
                QueueLimit = 0,
            });
    }
}
