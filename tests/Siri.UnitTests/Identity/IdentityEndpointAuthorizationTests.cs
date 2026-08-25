using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.Configuration;
using Siri.Modules.Identity;
using Siri.Modules.Notification;
using Siri.Persistence.DependencyInjection;

namespace Siri.UnitTests.Identity;

/// <summary>
/// Proves the P0-22 default-deny retrofit (backend.md: "MapGroup() ... ตั้ง .RequireAuthorization()
/// ที่ระดับ group แล้วค่อย .AllowAnonymous() เป็นราย endpoint") by inspecting the actual endpoint
/// metadata <see cref="IdentityModule.MapIdentityEndpoints"/> produces, via a bare
/// <see cref="WebApplication"/> built purely in-memory — no host is ever started, no configuration is
/// read, no database/Redis is touched, so this runs anywhere (no Docker needed) and can never
/// accidentally reach a real connection string the way booting the full <c>Program.cs</c> host would.
/// <para>
/// This is a stronger, more direct proof of the retrofit than an HTTP round trip would be: it reads
/// the actual route metadata the framework will use to make its allow/deny decision, rather than
/// inferring that decision from a response status code (which could pass for the wrong reason — e.g.
/// a request rejected by validation before authorization even ran).
/// </para>
/// </summary>
public class IdentityEndpointAuthorizationTests
{
    private static IReadOnlyList<RouteEndpoint> MapIdentityRouteEndpoints()
    {
        var builder = WebApplication.CreateBuilder();

        // Minimal API's RequestDelegateFactory resolves each endpoint delegate's non-body/non-special
        // parameters (here: each Feature's `Handler`) as DI services *while building endpoint
        // metadata* — i.e. as soon as .Endpoints below is enumerated, not just when a request actually
        // arrives — so it needs these types registered as services to correctly infer them, even
        // though this test never resolves or calls any of them. The exact same three calls Program.cs
        // itself uses; no configuration values are ever actually read because every config-dependent
        // read in these three (connection string, JWT signing key, Redis connection, ...) is deferred
        // inside a lazy factory/lambda or an options ValidateOnStart() check that only runs when a
        // host actually starts (see AddPersistence's AddDbContext lambda, AddIdentityModule's
        // IConnectionMultiplexer factory) — this test never calls app.RunAsync()/StartAsync(), so none
        // of that ever fires. An empty configuration is therefore safe and deliberate, not an
        // oversight.
        var emptyConfiguration = new ConfigurationBuilder().Build();
        builder.Services.AddPersistence(emptyConfiguration);
        builder.Services.AddIdentityModule(emptyConfiguration);
        builder.Services.AddNotificationModule(emptyConfiguration);

        var app = builder.Build();
        app.MapIdentityEndpoints();

        // WebApplication implements IEndpointRouteBuilder.DataSources explicitly, so it's only
        // reachable through the interface, not directly off the concrete WebApplication reference.
        IEndpointRouteBuilder endpointRouteBuilder = app;
        return endpointRouteBuilder.DataSources.SelectMany(dataSource => dataSource.Endpoints).Cast<RouteEndpoint>().ToList();
    }

    [Theory]
    [InlineData("/api/identity/register")]
    [InlineData("/api/identity/confirm-email")]
    [InlineData("/api/identity/login")]
    [InlineData("/api/identity/refresh")]
    [InlineData("/api/identity/forgot-password")]
    [InlineData("/api/identity/reset-password")]
    public void MapIdentityEndpoints_PublicEndpoint_CarriesGroupLevelAuthorizeMetadataAndExplicitAllowAnonymous(string routePattern)
    {
        var endpoints = MapIdentityRouteEndpoints();
        var endpoint = Assert.Single(endpoints, e => e.RoutePattern.RawText == routePattern);

        // The group's .RequireAuthorization() really is applied to this endpoint (default deny) ...
        Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        // ... but this specific endpoint explicitly opts back out of it.
        Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());
    }

    [Theory]
    [InlineData("/api/identity/sessions")]
    [InlineData("/api/identity/sessions/{sessionId:guid}")]
    [InlineData("/api/identity/sessions/revoke-others")]
    [InlineData("/api/identity/sessions/revoke-all")]
    [InlineData("/api/identity/admin/users")]
    [InlineData("/api/identity/admin/users/{userId:guid}/suspend")]
    [InlineData("/api/identity/admin/users/{userId:guid}/reactivate")]
    [InlineData("/api/identity/admin/users/{userId:guid}/roles")]
    [InlineData("/api/identity/admin/audit-logs")]
    public void MapIdentityEndpoints_ProtectedEndpoint_CarriesGroupLevelAuthorizeMetadataAndNoAllowAnonymous(string routePattern)
    {
        var endpoints = MapIdentityRouteEndpoints();
        var endpoint = Assert.Single(endpoints, e => e.RoutePattern.RawText == routePattern);

        Assert.NotEmpty(endpoint.Metadata.GetOrderedMetadata<IAuthorizeData>());
        Assert.Empty(endpoint.Metadata.GetOrderedMetadata<IAllowAnonymous>());
    }

    [Fact]
    public void MapIdentityEndpoints_MapsExactlyTheFifteenKnownEndpoints()
    {
        var endpoints = MapIdentityRouteEndpoints();

        Assert.Equal(15, endpoints.Count);
    }
}
