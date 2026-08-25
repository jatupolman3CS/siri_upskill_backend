using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Siri.Modules.Catalog.Features.GetRobotsTxt;

/// <summary>Maps GET /robots.txt — a root path, not nested under <c>/api/catalog</c> (see
/// <c>CatalogModule.MapCatalogEndpoints</c> for why: crawlers expect this at a fixed, well-known
/// site-root location). No <c>.AllowAnonymous()</c> needed — this is mapped directly on the root
/// <c>IEndpointRouteBuilder</c>, outside any <c>.RequireAuthorization()</c> group, so there is no
/// ambient requirement to opt out of (same reasoning <c>Siri.Api/Program.cs</c>'s own root-level
/// <c>MapHealthChecks("/health")</c> already relies on).</summary>
public static class GetRobotsTxtEndpoint
{
    public static IEndpointRouteBuilder MapGetRobotsTxtEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/robots.txt", (GetRobotsTxtHandler handler) => Results.Text(handler.Handle(), "text/plain"))
            .WithName("SeoGetRobotsTxt")
            .WithSummary("robots.txt สำหรับ search engine crawler")
            .Produces<string>(StatusCodes.Status200OK, "text/plain");

        return endpoints;
    }
}
