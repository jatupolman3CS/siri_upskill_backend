using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Features.GetRobotsTxt;
using Siri.Modules.Catalog.Infrastructure;

namespace Siri.UnitTests.Catalog;

/// <summary>Unlike every other Catalog handler, this one needs no <c>AppDbContext</c> — pure string
/// formatting from <see cref="SeoOptions"/> — so it's the first one in this module testable without a
/// live database.</summary>
public class GetRobotsTxtHandlerTests
{
    private static GetRobotsTxtHandler CreateHandler(string publicBaseUrl) =>
        new(Options.Create(new SeoOptions { PublicBaseUrl = publicBaseUrl }));

    [Fact]
    public void Handle_DisallowsCsrOnlyAppAreas()
    {
        var result = CreateHandler("https://siriupskill.test").Handle();

        Assert.Contains("Disallow: /learn", result);
        Assert.Contains("Disallow: /instructor", result);
        Assert.Contains("Disallow: /admin", result);
        Assert.Contains("Disallow: /api/", result);
    }

    [Fact]
    public void Handle_ReferencesSitemapAtPublicBaseUrl()
    {
        var result = CreateHandler("https://siriupskill.test").Handle();

        Assert.Contains("Sitemap: https://siriupskill.test/sitemap.xml", result);
    }

    [Fact]
    public void Handle_PublicBaseUrlWithTrailingSlash_DoesNotProduceDoubleSlash()
    {
        var result = CreateHandler("https://siriupskill.test/").Handle();

        Assert.Contains("Sitemap: https://siriupskill.test/sitemap.xml", result);
        Assert.DoesNotContain("//sitemap.xml", result);
    }
}
