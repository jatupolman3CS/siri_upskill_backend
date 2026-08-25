using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Siri.Modules.Catalog.Features.GetCoursesSitemapPage;

/// <summary>Maps GET /sitemap-courses-{page}.xml — a root path, not nested under <c>/api/catalog</c>
/// (see <c>CatalogModule.MapCatalogEndpoints</c> for why). No <c>.AllowAnonymous()</c> needed — same
/// reasoning <c>GetRobotsTxtEndpoint</c>'s own doc comment gives.</summary>
public static class GetCoursesSitemapPageEndpoint
{
    public static IEndpointRouteBuilder MapGetCoursesSitemapPageEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet(
                "/sitemap-courses-{page:int}.xml",
                async (int page, GetCoursesSitemapPageHandler handler, CancellationToken cancellationToken) =>
                    Results.Text(await handler.HandleAsync(page, cancellationToken).ConfigureAwait(false), "application/xml"))
            .WithName("SeoGetCoursesSitemapPage")
            .WithSummary("Sitemap หนึ่งหน้าของรายการคอร์สที่เผยแพร่แล้ว")
            .Produces<string>(StatusCodes.Status200OK, "application/xml");

        return endpoints;
    }
}
