using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Api.Configuration;
using Xunit;

namespace Siri.IntegrationTests;

/// <summary>
/// D5 (integrator-qa): over HTTP, with the exact production rate-limiter configuration (<see cref="RateLimiterConfiguration.Configure"/> — what
/// <c>Program</c> registers), a refused request gets an RFC 9457 429 body, <c>Retry-After</c> and <c>Cache-Control: no-store</c> — for a global fixed-window
/// policy ("auth") and for the partitioned per-user ones ("heartbeat", "live-join"). Self-contained (TestServer, no database/Redis/Docker).
/// </summary>
public sealed class RateLimiterRejectionIntegrationTests
{
    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var userId) || string.IsNullOrEmpty(userId))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var identity = new ClaimsIdentity([new Claim(ClaimTypes.NameIdentifier, userId.ToString())], "Test");
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(new ClaimsPrincipal(identity), "Test")));
        }
    }

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthentication("Test").AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });
        builder.Services.AddRateLimiter(options => RateLimiterConfiguration.Configure(options, isDevelopment: false));

        var app = builder.Build();
        app.UseAuthentication();
        app.UseRateLimiter();

        app.MapPost("/auth", () => Results.Ok()).RequireRateLimiting("auth");
        app.MapPut("/heartbeat", () => Results.Ok()).RequireRateLimiting(RateLimiterConfiguration.HeartbeatPolicyName);
        app.MapGet("/live-join", () => Results.Ok()).RequireRateLimiting(RateLimiterConfiguration.LiveJoinPolicyName);

        await app.StartAsync();
        return app;
    }

    private static async Task AssertProblem429Async(HttpResponseMessage response)
    {
        Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);

        var retryAfter = Assert.Single(response.Headers.GetValues("Retry-After"));
        Assert.True(int.TryParse(retryAfter, out var seconds) && seconds >= 1, $"Retry-After was '{retryAfter}'");

        using var body = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.Equal(429, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal("rate_limited", body.RootElement.GetProperty("errorCode").GetString());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("traceId").GetString()));
        Assert.False(body.RootElement.TryGetProperty("reason", out _));
    }

    [Fact]
    public async Task GlobalFixedWindowPolicy_SixthRequestIsAProblem429WithRetryAfterAndNoStore()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();

        for (var i = 1; i <= 5; i++)
        {
            using var ok = await client.PostAsync("/auth", content: null);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
            Assert.Null(ok.Headers.CacheControl); // only the refusal is stamped
        }

        using var rejected = await client.PostAsync("/auth", content: null);

        await AssertProblem429Async(rejected);
    }

    [Fact]
    public async Task PartitionedHeartbeatPolicy_SeventhRequestIsAProblem429_AndAnotherUserIsUnaffected()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "learner-1");

        for (var i = 1; i <= 6; i++)
        {
            using var ok = await client.PutAsync("/heartbeat", content: null);
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        using var rejected = await client.PutAsync("/heartbeat", content: null);
        await AssertProblem429Async(rejected);

        using var other = app.GetTestClient();
        other.DefaultRequestHeaders.Add("X-Test-User", "learner-2");
        using var otherOk = await other.PutAsync("/heartbeat", content: null);
        Assert.Equal(HttpStatusCode.OK, otherOk.StatusCode);
    }

    [Fact]
    public async Task PartitionedLiveJoinPolicy_SeventhRequestIsAProblem429()
    {
        await using var app = await StartAsync();
        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "learner-1");

        for (var i = 1; i <= 6; i++)
        {
            using var ok = await client.GetAsync("/live-join");
            Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        }

        using var rejected = await client.GetAsync("/live-join");

        await AssertProblem429Async(rejected);
    }
}
