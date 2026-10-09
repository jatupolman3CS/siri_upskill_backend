using System.ComponentModel.DataAnnotations;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Infrastructure.Search;

namespace Siri.UnitTests.Catalog.Search;

/// <summary>
/// <see cref="MeilisearchOptions"/>: when the feature counts as ON, and which misconfigurations are loud (boot failure) versus quiet (the PostgreSQL
/// fallback is used). The rule: a missing URL/key means "not configured" and is fine; a URL that cannot be right is an error.
/// </summary>
public sealed class MeilisearchOptionsTests
{
    private static MeilisearchOptions Valid() => new() { Url = "http://meili.test:7700", ApiKey = "a-real-key" };

    [Fact]
    public void IsActive_UrlAndRealKey_IsTrue() => Assert.True(Valid().IsActive);

    [Theory]
    [InlineData("", "a-real-key")]
    [InlineData("   ", "a-real-key")]
    [InlineData("http://meili.test:7700", "")]
    [InlineData("http://meili.test:7700", "   ")]
    [InlineData("http://meili.test:7700", "CHANGE_ME")]
    [InlineData("http://meili.test:7700", "change_me_please")]
    public void IsActive_MissingUrlOrPlaceholderKey_IsFalse(string url, string apiKey)
    {
        var options = new MeilisearchOptions { Url = url, ApiKey = apiKey };

        Assert.False(options.IsActive);
        Assert.False(string.IsNullOrWhiteSpace(options.InactiveReason));
    }

    [Fact]
    public void IsActive_EnabledFalse_OverridesEverythingElse()
    {
        var options = Valid();
        options.Enabled = false;

        Assert.False(options.IsActive);
        Assert.Contains("Enabled", options.InactiveReason, StringComparison.Ordinal);
    }

    [Fact]
    public void InactiveReason_NeverContainsTheApiKey_AndIsNullWhenActive()
    {
        var placeholder = new MeilisearchOptions { Url = "http://meili.test:7700", ApiKey = "hunter2-CHANGE_ME" };

        Assert.False(placeholder.IsActive);
        Assert.DoesNotContain("hunter2", placeholder.InactiveReason, StringComparison.Ordinal);
        Assert.Null(Valid().InactiveReason);
    }

    [Theory]
    [InlineData("http://158.178.243.69:30700", "http://158.178.243.69:30700/")]
    [InlineData("http://158.178.243.69:30700/", "http://158.178.243.69:30700/")]
    [InlineData("  https://search.example.test  ", "https://search.example.test/")]
    [InlineData("https://search.example.test/base///", "https://search.example.test/base/")]
    public void GetBaseAddress_NormalisesToExactlyOneTrailingSlash(string url, string expected) =>
        Assert.Equal(expected, new MeilisearchOptions { Url = url }.GetBaseAddress()?.ToString());

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("ftp://meili.test")]
    [InlineData("meili.test:7700")]
    public void GetBaseAddress_UnusableUrl_IsNull(string url) =>
        Assert.Null(new MeilisearchOptions { Url = url }.GetBaseAddress());

    [Fact]
    public void Validator_NoUrlAtAll_IsAcceptedBecauseTheFeatureIsSimplyOff() =>
        Assert.True(new MeilisearchOptionsValidator().Validate(null, new MeilisearchOptions()).Succeeded);

    [Fact]
    public void Validator_UrlWithoutKey_IsAcceptedAndFallsBackToTheDatabase() =>
        Assert.True(new MeilisearchOptionsValidator().Validate(null, new MeilisearchOptions { Url = "http://meili.test:7700" }).Succeeded);

    [Theory]
    [InlineData("not a url")]
    [InlineData("ftp://meili.test")]
    [InlineData("meili.test:7700")]
    public void Validator_MalformedUrl_FailsSoATypoCannotSilentlyDisableSearch(string url)
    {
        var result = new MeilisearchOptionsValidator().Validate(null, new MeilisearchOptions { Url = url });

        Assert.True(result.Failed);
        Assert.Contains("Meilisearch:Url", result.FailureMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("siriupskill_documents")]
    [InlineData("docs-2")]
    [InlineData("A")]
    public void Validator_ValidIndexUid_Passes(string uid) =>
        Assert.True(new MeilisearchOptionsValidator().Validate(null, new MeilisearchOptions { DocumentsIndexUid = uid }).Succeeded);

    [Theory]
    [InlineData("")]
    [InlineData("has space")]
    [InlineData("slash/uid")]
    [InlineData("dot.uid")]
    [InlineData("ไทย")]
    public void Validator_InvalidIndexUid_Fails(string uid) =>
        Assert.True(new MeilisearchOptionsValidator().Validate(null, new MeilisearchOptions { DocumentsIndexUid = uid }).Failed);

    [Fact]
    public void Defaults_MatchTheDocumentedEnvironment()
    {
        var options = new MeilisearchOptions();

        Assert.True(options.Enabled);
        Assert.Equal("siriupskill_documents", options.DocumentsIndexUid);
        Assert.False(options.IsActive);
        Assert.Empty(Validate(options));
    }

    [Theory]
    [InlineData(nameof(MeilisearchOptions.SearchTimeoutMilliseconds), 10)]
    [InlineData(nameof(MeilisearchOptions.IndexBatchSize), 0)]
    [InlineData(nameof(MeilisearchOptions.MaxSearchHits), 1)]
    [InlineData(nameof(MeilisearchOptions.RequestTimeoutSeconds), 0)]
    public void DataAnnotations_OutOfRangeNumbers_AreRejected(string property, int value)
    {
        var options = new MeilisearchOptions();
        typeof(MeilisearchOptions).GetProperty(property)!.SetValue(options, value);

        Assert.NotEmpty(Validate(options));
    }

    private static List<ValidationResult> Validate(MeilisearchOptions options)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(options, new ValidationContext(options), results, validateAllProperties: true);
        return results;
    }
}
