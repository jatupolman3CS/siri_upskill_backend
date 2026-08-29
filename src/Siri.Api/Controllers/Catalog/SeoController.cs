using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Catalog.Features.GetCoursesSitemapPage;
using Siri.Modules.Catalog.Features.GetRobotsTxt;
using Siri.Modules.Catalog.Features.GetSitemapIndex;

namespace Siri.Api.Controllers.Catalog;

[ApiController]
[AllowAnonymous]
[Tags("SEO")]
public class SeoController : ControllerBase
{
    [HttpGet("/robots.txt")]
    [EndpointName("SeoGetRobotsTxt")]
    [EndpointSummary("robots.txt สำหรับ search engine crawler")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "text/plain")]
    public IResult GetRobotsTxt([FromServices] GetRobotsTxtHandler handler)
    {
        return Results.Text(handler.Handle(), "text/plain");
    }

    [HttpGet("/sitemap.xml")]
    [EndpointName("SeoGetSitemapIndex")]
    [EndpointSummary("Sitemap index รวมทุกหน้าของคอร์สที่เผยแพร่แล้ว")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "application/xml")]
    public async Task<IResult> GetSitemapIndex(
        [FromServices] GetSitemapIndexHandler handler,
        CancellationToken cancellationToken)
    {
        var xml = await handler.HandleAsync(cancellationToken).ConfigureAwait(false);
        return Results.Text(xml, "application/xml");
    }

    [HttpGet("/sitemap-courses-{page:int}.xml")]
    [EndpointName("SeoGetCoursesSitemapPage")]
    [EndpointSummary("Sitemap หนึ่งหน้าของรายการคอร์สที่เผยแพร่แล้ว")]
    [ProducesResponseType(typeof(string), StatusCodes.Status200OK, "application/xml")]
    public async Task<IResult> GetCoursesSitemapPage(
        [FromRoute] int page,
        [FromServices] GetCoursesSitemapPageHandler handler,
        CancellationToken cancellationToken)
    {
        var xml = await handler.HandleAsync(page, cancellationToken).ConfigureAwait(false);
        return Results.Text(xml, "application/xml");
    }
}
