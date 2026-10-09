using System.Net.Http.Headers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Infrastructure.Search;
using Siri.SharedKernel;

namespace Siri.UnitTests.Catalog.Search;

/// <summary>
/// The real <c>AddCatalogModule</c> registration, driven by the environment variables the deployment sets
/// (<c>Meilisearch__Url</c>, <c>Meilisearch__ApiKey</c>, <c>Meilisearch__DocumentsIndexUid</c> — here as the equivalent configuration keys): which
/// <see cref="ICourseSearchIndex"/> a request gets, and that the typed HTTP client really carries the base address and bearer key.
/// </summary>
public sealed class MeilisearchWiringTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IClock>(new FakeClock(DateTime.UtcNow));
        services.AddCatalogModule(configuration);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void UrlAndRealKey_ResolveTheMeilisearchIndex_WithBaseAddressAndBearerKeyOnTheClient()
    {
        using var provider = Build(new()
        {
            ["Meilisearch:Url"] = "http://158.178.243.69:30700",
            ["Meilisearch:ApiKey"] = "a-real-key",
            ["Meilisearch:DocumentsIndexUid"] = "siriupskill_documents",
        });
        using var scope = provider.CreateScope();

        var index = scope.ServiceProvider.GetRequiredService<ICourseSearchIndex>();

        Assert.IsType<MeilisearchCourseSearchIndex>(index);
        Assert.True(index.IsEnabled);

        var client = scope.ServiceProvider.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(MeilisearchCourseSearchIndex));
        Assert.Equal(new Uri("http://158.178.243.69:30700/"), client.BaseAddress);
        Assert.Equal(new AuthenticationHeaderValue("Bearer", "a-real-key"), client.DefaultRequestHeaders.Authorization);
        Assert.Equal(TimeSpan.FromSeconds(15), client.Timeout);
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", "")]
    [InlineData("http://158.178.243.69:30700", null)]
    [InlineData("http://158.178.243.69:30700", "")]
    [InlineData("http://158.178.243.69:30700", "CHANGE_ME")]
    [InlineData(null, "a-real-key")]
    public void NotFullyConfigured_ResolvesTheDisabledIndex_SoSearchStaysOnPostgreSql(string? url, string? apiKey)
    {
        using var provider = Build(new() { ["Meilisearch:Url"] = url, ["Meilisearch:ApiKey"] = apiKey });
        using var scope = provider.CreateScope();

        var index = scope.ServiceProvider.GetRequiredService<ICourseSearchIndex>();

        Assert.IsType<DisabledCourseSearchIndex>(index);
        Assert.False(index.IsEnabled);
    }

    [Fact]
    public void EnabledFalse_ResolvesTheDisabledIndexEvenWhenEverythingIsConfigured()
    {
        using var provider = Build(new()
        {
            ["Meilisearch:Enabled"] = "false",
            ["Meilisearch:Url"] = "http://158.178.243.69:30700",
            ["Meilisearch:ApiKey"] = "a-real-key",
        });
        using var scope = provider.CreateScope();

        Assert.IsType<DisabledCourseSearchIndex>(scope.ServiceProvider.GetRequiredService<ICourseSearchIndex>());
    }

    [Fact]
    public void OptionsBindFromTheEnvironmentStyleKeys_IncludingTheDocumentsIndexUid()
    {
        using var provider = Build(new()
        {
            ["Meilisearch:Url"] = "http://158.178.243.69:30700",
            ["Meilisearch:ApiKey"] = "a-real-key",
            ["Meilisearch:DocumentsIndexUid"] = "siriupskill_documents",
        });

        var options = provider.GetRequiredService<IOptions<MeilisearchOptions>>().Value;

        Assert.Equal("siriupskill_documents", options.DocumentsIndexUid);
        Assert.True(options.IsActive);
    }

    [Fact]
    public void MalformedUrl_FailsOptionsValidation_SoATypoIsNotMistakenForAnIntentionalOff()
    {
        using var provider = Build(new() { ["Meilisearch:Url"] = "158.178.243.69:30700", ["Meilisearch:ApiKey"] = "a-real-key" });

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<MeilisearchOptions>>().Value);

        Assert.Contains("Meilisearch:Url", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void TheAvailabilityMemory_IsOneSingleton_SoTheCircuitBreakerIsSharedByEveryRequest()
    {
        using var provider = Build(new()
        {
            ["Meilisearch:Url"] = "http://158.178.243.69:30700",
            ["Meilisearch:ApiKey"] = "a-real-key",
        });

        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Assert.Same(
            first.ServiceProvider.GetRequiredService<MeilisearchAvailability>(),
            second.ServiceProvider.GetRequiredService<MeilisearchAvailability>());
    }

    [Fact]
    public void TheIndexerReindexJobAndBootstrapperAreRegistered()
    {
        var services = new ServiceCollection();
        services.AddCatalogModule(new ConfigurationBuilder().Build());

        Assert.Contains(services, d => d.ServiceType == typeof(CourseSearchIndexer));
        Assert.Contains(services, d => d.ServiceType == typeof(CourseSearchReindexJob));
        Assert.Contains(
            services,
            d => d.ServiceType == typeof(Microsoft.Extensions.Hosting.IHostedService) && d.ImplementationType == typeof(CourseSearchIndexBootstrapper));
    }
}
