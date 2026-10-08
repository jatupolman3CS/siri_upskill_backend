using System.Diagnostics;
using System.Globalization;
using System.Security.Claims;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;

namespace Siri.Api.Configuration;

/// <summary>
/// Rate limiting policies and partitioners for <see cref="Program"/>.
/// </summary>
public static class RateLimiterConfiguration
{
    /// <summary><c>errorCode</c> of every 429 ProblemDetails body.</summary>
    public const string RateLimitedErrorCode = "rate_limited";

    public const string HeartbeatPolicyName = "heartbeat";

    /// <summary>Join-the-live-room requests (P11-05): 6 per minute per user.</summary>
    public const string LiveJoinPolicyName = "live-join";

    /// <summary>Every other signed-in Live endpoint (status, connect, disconnect, meetings, meeting-link, resync, ...): 60 per minute per user.</summary>
    public const string LiveUserPolicyName = "live-user";

    /// <summary>The anonymous Google OAuth callback: 60 per minute per client address.</summary>
    public const string LiveGoogleCallbackPolicyName = "live-google-callback";

    /// <summary>Read-only signed-in Payout endpoints (currently <c>GET /api/payout/policy</c>): 60 per minute per user.</summary>
    public const string PayoutReadPolicyName = "payout-read";

    /// <summary>
    /// Registers every policy of the API and the shared 429 response (<see cref="WriteRejectedResponseAsync"/>) — the single place
    /// <c>Program</c> configures the rate limiter, so a test can build the exact production configuration.
    /// </summary>
    public static void Configure(RateLimiterOptions options, bool isDevelopment)
    {
        ArgumentNullException.ThrowIfNull(options);

        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        // Every policy below (and the per-user ones): a 429 is an RFC 9457 ProblemDetails + Retry-After + Cache-Control: no-store,
        // never an empty body. The callback is global, so a new policy needs nothing extra.
        options.OnRejected = WriteRejectedResponseAsync;

        // "auth": applied via .RequireRateLimiting("auth") on Identity's Register/ConfirmEmail (P0-15)
        // and now Login/Refresh (P0-16) too. Still global/unpartitioned (not per-IP/per-key), so it
        // throttles each endpoint as a whole rather than each caller individually — partitioning is a
        // separate, cross-cutting change since it would affect every endpoint already on this policy.
        options.AddFixedWindowLimiter("auth", limiter =>
        {
            limiter.Window = TimeSpan.FromMinutes(1);
            // Local navigation and reloads share this limiter with login and token refresh.
            limiter.PermitLimit = isDevelopment ? 100 : 5;
            limiter.QueueLimit = 0;
        });

        // "default": not yet applied to any endpoint — reserved for non-auth public endpoints
        // (browse/catalog) once they need one, per ARCHITECTURE.md §2 "Rate limit".
        options.AddFixedWindowLimiter("default", limiter =>
        {
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.PermitLimit = 100;
            limiter.QueueLimit = 0;
        });

        // "webhook": applied via .RequireRateLimiting("webhook") on payment webhook endpoints (P3-04).
        options.AddFixedWindowLimiter("webhook", limiter =>
        {
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.PermitLimit = 120;
            limiter.QueueLimit = 0;
        });

        // "heartbeat": applied to playback progress heartbeat endpoints (X-29). Partitioned per user
        // so that concurrent learners never exhaust a global quota. 6 requests per 30 seconds allows
        // the normal 15s interval (2 requests/30s) plus bursts from seek/resume events.
        options.AddPolicy<string>(HeartbeatPolicyName, CreateHeartbeatPartition);

        // Live (P11-03/05): partitioned per user (per client address for the anonymous OAuth callback), so one
        // person's traffic can never use up anyone else's quota — unlike the app-wide "default" window above, which
        // the Live endpoints must not use.
        options.AddPolicy<string>(LiveJoinPolicyName, CreateLiveJoinPartition);
        options.AddPolicy<string>(LiveUserPolicyName, CreateLiveUserPartition);
        options.AddPolicy<string>(LiveGoogleCallbackPolicyName, CreateLiveGoogleCallbackPartition);

        // Payout reads: partitioned per user for the same reason as the Live ones — never the app-wide "default" window.
        options.AddPolicy<string>(PayoutReadPolicyName, CreatePayoutReadPartition);
    }

    /// <summary>
    /// <c>RateLimiterOptions.OnRejected</c> for <b>every</b> policy (the callback is global): answers 429 with an RFC 9457 ProblemDetails body
    /// (<c>errorCode: "rate_limited"</c> + <c>traceId</c>, like every other error), a <c>Retry-After</c> header in whole seconds when the limiter
    /// reported when the window reopens, and <c>Cache-Control: no-store</c> so no cache or proxy can hold on to the refusal and replay it to someone else.
    /// No <c>reason</c> member on purpose — clients recognise a rate limit by status alone.
    /// </summary>
    public static async ValueTask WriteRejectedResponseAsync(OnRejectedContext context, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);

        var httpContext = context.HttpContext;
        var response = httpContext.Response;

        response.StatusCode = StatusCodes.Status429TooManyRequests;
        response.Headers.CacheControl = "no-store";

        // Only a limiter that knows when it reopens reports it (fixed/sliding windows do, concurrency limiters do not): never invent a value.
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter) && retryAfter > TimeSpan.Zero)
        {
            var seconds = Math.Max(1, (int)Math.Ceiling(retryAfter.TotalSeconds));
            response.Headers.RetryAfter = seconds.ToString(CultureInfo.InvariantCulture);
        }

        await Results.Problem(
                statusCode: StatusCodes.Status429TooManyRequests,
                title: "Too many requests",
                extensions: new Dictionary<string, object?>
                {
                    ["traceId"] = Activity.Current?.Id ?? httpContext.TraceIdentifier,
                    ["errorCode"] = RateLimitedErrorCode,
                })
            .ExecuteAsync(httpContext)
            .ConfigureAwait(false);
    }

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

    /// <summary>Partitioned per authenticated user (6 requests per 60 seconds) so one learner cannot exhaust anyone else's quota.</summary>
    public static RateLimitPartition<string> CreateLiveJoinPartition(HttpContext httpContext) =>
        CreateUserPartition(httpContext, "live-join", permitLimit: 6, TimeSpan.FromSeconds(60));

    /// <summary>Partitioned per authenticated user (60 requests per 60 seconds).</summary>
    public static RateLimitPartition<string> CreateLiveUserPartition(HttpContext httpContext) =>
        CreateUserPartition(httpContext, "live-user", permitLimit: 60, TimeSpan.FromSeconds(60));

    /// <summary>Partitioned per authenticated user (60 requests per 60 seconds).</summary>
    public static RateLimitPartition<string> CreatePayoutReadPartition(HttpContext httpContext) =>
        CreateUserPartition(httpContext, "payout-read", permitLimit: 60, TimeSpan.FromSeconds(60));

    /// <summary>
    /// Partitioned per client address (60 requests per 60 seconds) — the OAuth callback is anonymous, so there is no user to key on.
    /// Behind a reverse proxy without forwarded-header handling every caller shares the proxy's address (a known, accepted limitation
    /// while the callback is low-volume).
    /// </summary>
    public static RateLimitPartition<string> CreateLiveGoogleCallbackPartition(HttpContext httpContext)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        var client = httpContext.Connection.RemoteIpAddress?.ToString() ?? "anonymous";
        return CreateFixedWindowPartition($"live-google-callback:{client}", permitLimit: 60, TimeSpan.FromSeconds(60));
    }

    private static RateLimitPartition<string> CreateUserPartition(HttpContext httpContext, string policy, int permitLimit, TimeSpan window)
    {
        ArgumentNullException.ThrowIfNull(httpContext);

        // The policy name is part of the key so a user's "live-join" budget is independent of their "live-user" budget.
        var user = httpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value
            ?? httpContext.User.FindFirst("sub")?.Value
            ?? httpContext.Connection.RemoteIpAddress?.ToString()
            ?? "anonymous";

        return CreateFixedWindowPartition($"{policy}:{user}", permitLimit, window);
    }

    private static RateLimitPartition<string> CreateFixedWindowPartition(string key, int permitLimit, TimeSpan window) =>
        RateLimitPartition.GetFixedWindowLimiter(
            key,
            _ => new FixedWindowRateLimiterOptions
            {
                Window = window,
                PermitLimit = permitLimit,
                QueueLimit = 0,
            });
}
