using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Infrastructure;

namespace Siri.Modules.Catalog.Features.GetRobotsTxt;

/// <summary>
/// Task P1-09 ("SEO backend"): <c>robots.txt</c>. Disallows the CSR-only, <c>noindex</c> app areas
/// frontend.md already names ("/learn, /instructor, /admin เป็น CSR + noindex") — robots.txt keeps
/// crawlers from even fetching those paths in the first place, a step earlier than the per-page
/// <c>noindex</c> meta tag those pages are also expected to carry. References the sitemap index's own
/// route (<c>Features.GetSitemapIndex</c>) so crawlers can discover every published course without
/// needing to follow internal links.
/// </summary>
public sealed class GetRobotsTxtHandler(IOptions<SeoOptions> seoOptions)
{
    public string Handle()
    {
        var baseUrl = seoOptions.Value.PublicBaseUrl.TrimEnd('/');

        return $"""
            User-agent: *
            Disallow: /learn
            Disallow: /instructor
            Disallow: /admin
            Disallow: /api/

            Sitemap: {baseUrl}/sitemap.xml
            """;
    }
}
