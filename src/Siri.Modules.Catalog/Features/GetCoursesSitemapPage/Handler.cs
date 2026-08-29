using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Features.GetCoursesSitemapPage;

/// <summary>
/// One page of a paginated course sitemap (task P1-09: "sitemap.xml (แบ่งหน้า)") — <see cref="PageSize"/>
/// URLs per file, well under the sitemap protocol's 50,000-URL cap even at docs/DECISIONS.md D-10's
/// stated catalog ceiling ("&lt; ~10k คอร์ส"). The sitemap index tells a crawler how many pages exist;
/// this handler doesn't validate the requested page number against that count — an out-of-range page
/// simply returns an empty, still-valid <c>&lt;urlset&gt;</c>, which is harmless.
/// <para>
/// URLs point at the frontend's course-detail page — assumed to be <c>{PublicBaseUrl}/courses/{slug}</c>,
/// matching this API's own course-detail route pattern, since the actual frontend page (task P1-22)
/// doesn't exist yet to confirm against. If P1-22 lands with a different URL pattern, this is the one
/// place that needs to change to match.
/// </para>
/// </summary>
public sealed class GetCoursesSitemapPageHandler(AppDbContext dbContext, IOptions<SeoOptions> seoOptions)
{
    public const int PageSize = 5000;

    private static readonly XNamespace SitemapNamespace = "http://www.sitemaps.org/schemas/sitemap/0.9";

    public async Task<string> HandleAsync(int page, CancellationToken cancellationToken)
    {
        var effectivePage = page < 1 ? 1 : page;
        var baseUrl = seoOptions.Value.PublicBaseUrl.TrimEnd('/');

        var courses = await dbContext.Courses()
            .AsNoTracking()
            .Where(c => c.Status == CourseStatus.Published)
            .OrderBy(c => c.Id) // stable order across pages, same reasoning any keyset-style pagination needs
            .Skip((effectivePage - 1) * PageSize)
            .Take(PageSize)
            // PublishedAtUtc is guaranteed set here — COURSE.Publish always sets it, and this query is
            // already filtered to Status == Published.
            .Select(c => new { c.Slug, PublishedAtUtc = c.PublishedAtUtc!.Value })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var urlset = new XElement(
            SitemapNamespace + "urlset",
            courses.Select(c => new XElement(
                SitemapNamespace + "url",
                new XElement(SitemapNamespace + "loc", $"{baseUrl}/courses/{c.Slug}"),
                new XElement(SitemapNamespace + "lastmod", c.PublishedAtUtc.ToString("yyyy-MM-ddTHH:mm:ssZ")))));

        // Plain concatenation, not a raw string literal — XDocument.ToString() omits the <?xml ...?>
        // prolog by default regardless of any XDeclaration set on it (a well-known gotcha), so this
        // builds it by hand instead of fighting that.
        return "<?xml version=\"1.0\" encoding=\"UTF-8\"?>" + Environment.NewLine + urlset;
    }
}
