using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.GetCoursesSitemapPage;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Features.GetSitemapIndex;

/// <summary>
/// Task P1-09 ("SEO backend"): the sitemap index — lists every course sub-sitemap page (see
/// <see cref="GetCoursesSitemapPageHandler"/>'s own doc comment for the URL pattern each page uses and
/// why). Always at least one page, even with zero published courses — an empty
/// <c>sitemap-courses-1.xml</c> is still valid, and that's simpler than special-casing "no courses yet"
/// out of the index entirely.
/// </summary>
public sealed class GetSitemapIndexHandler(AppDbContext dbContext, IOptions<SeoOptions> seoOptions)
{
    private static readonly XNamespace SitemapNamespace = "http://www.sitemaps.org/schemas/sitemap/0.9";

    public async Task<string> HandleAsync(CancellationToken cancellationToken)
    {
        var baseUrl = seoOptions.Value.PublicBaseUrl.TrimEnd('/');

        var publishedCourseCount = await dbContext.Courses()
            .AsNoTracking()
            .CountAsync(c => c.Status == CourseStatus.Published, cancellationToken)
            .ConfigureAwait(false);

        var pageCount = Math.Max(1, (int)Math.Ceiling(publishedCourseCount / (double)GetCoursesSitemapPageHandler.PageSize));

        var sitemapIndex = new XElement(
            SitemapNamespace + "sitemapindex",
            Enumerable.Range(1, pageCount).Select(page => new XElement(
                SitemapNamespace + "sitemap",
                new XElement(SitemapNamespace + "loc", $"{baseUrl}/sitemap-courses-{page}.xml"))));

        // Plain concatenation, not a raw string literal — XDocument.ToString() omits the <?xml ...?>
        // prolog by default regardless of any XDeclaration set on it (a well-known gotcha), so this
        // builds it by hand instead of fighting that.
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Environment.NewLine + sitemapIndex;
    }
}
