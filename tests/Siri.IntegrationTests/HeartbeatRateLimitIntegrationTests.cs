using System.Net;
using System.Security.Claims;
using System.Text.Encodings.Web;
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

public sealed class HeartbeatRateLimitIntegrationTests
{
    private sealed class TestAuthHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder) : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
    {
        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            if (!Request.Headers.TryGetValue("X-Test-User", out var userIdValue) || string.IsNullOrEmpty(userIdValue))
            {
                return Task.FromResult(AuthenticateResult.NoResult());
            }

            var claims = new[]
            {
                new Claim(ClaimTypes.NameIdentifier, userIdValue.ToString()),
            };
            var identity = new ClaimsIdentity(claims, "Test");
            var principal = new ClaimsPrincipal(identity);
            var ticket = new AuthenticationTicket(principal, "Test");

            return Task.FromResult(AuthenticateResult.Success(ticket));
        }
    }

    [Fact]
    public async Task HeartbeatEndpoint_Enforces429_OnSeventhRequestOverHttp()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Services.AddAuthentication("Test")
            .AddScheme<AuthenticationSchemeOptions, TestAuthHandler>("Test", _ => { });

        builder.Services.AddRateLimiter(options =>
        {
            options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
            options.AddPolicy<string>(
                RateLimiterConfiguration.HeartbeatPolicyName,
                RateLimiterConfiguration.CreateHeartbeatPartition);
        });

        var app = builder.Build();

        app.UseAuthentication();
        app.UseRateLimiter();

        app.MapPut("/test-heartbeat", () => Results.Ok(new { status = "ok" }))
            .RequireRateLimiting(RateLimiterConfiguration.HeartbeatPolicyName);

        await app.StartAsync();

        var client = app.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Test-User", "learner-1");

        // 1-6 requests succeed with 200 OK
        for (var i = 1; i <= 6; i++)
        {
            using var response = await client.PutAsync("/test-heartbeat", null);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        // 7th request from same learner gets 429 Too Many Requests
        using var rejected = await client.PutAsync("/test-heartbeat", null);
        Assert.Equal(HttpStatusCode.TooManyRequests, rejected.StatusCode);

        // A different learner still gets 200 OK
        using var client2 = app.GetTestClient();
        client2.DefaultRequestHeaders.Add("X-Test-User", "learner-2");
        using var responseLearner2 = await client2.PutAsync("/test-heartbeat", null);
        Assert.Equal(HttpStatusCode.OK, responseLearner2.StatusCode);

        await app.StopAsync();
    }
}
