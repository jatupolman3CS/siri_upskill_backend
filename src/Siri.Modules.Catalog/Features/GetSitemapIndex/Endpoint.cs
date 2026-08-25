using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Siri.Modules.Catalog.Features.GetSitemapIndex;

/// <summary>Maps GET /sitemap.xml — a root path, not nested under <c>/api/catalog</c> (see
/// <c>CatalogModule.MapCatalogEndpoints</c> for why). No <c>.AllowAnonymous()</c> needed — same
/// reasoning <c>GetRobotsTxtEndpoint</c>'s own doc comment gives.</summary>
public static class GetSitemapIndexEndpoint
{
    public static IEndpointRouteBuilder MapGetSitemapIndexEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/sitemap.xml",
                async (GetSitemapIndexHandler handler, CancellationToken cancellationToken) =>
                    Results.Text(await handler.HandleAsync(cancellationToken).ConfigureAwait(false), "application/xml"))
            .WithName("SeoGetSitemapIndex")
            .WithSummary("Sitemap index รวมทุกหน้าของคอร์สที่เผยแพร่แล้ว")
            .Produces<string>(StatusCodes.Status200OK, "application/xml");

        return endpoints;
    }
}
