using Siri.Integrations.Video.Bunny;

namespace Siri.UnitTests.Video;

public class VideoProviderOptionsTests
{
    private static VideoProviderOptions FullyConfigured() => new()
    {
        LibraryId = "12345",
        ApiKey = "test-api-key",
        ReadOnlyApiKey = "test-readonly-key",
        PullZone = "my-pull-zone",
        CdnHostname = "my-pull-zone.b-cdn.net",
        TokenAuthenticationKey = "test-token-auth-key",
    };

    [Fact]
    public void Defaults_AreEmpty_AndEverythingIsReportedMissing()
    {
        // Nothing is [Required]: an unconfigured host boots, and each feature reports exactly what it lacks.
        var options = new VideoProviderOptions();

        Assert.Equal(["VideoProvider:LibraryId", "VideoProvider:ApiKey"], options.GetMissingApiSettings());
        Assert.Equal(["VideoProvider:CdnHostname", "VideoProvider:TokenAuthenticationKey"], options.GetMissingPlaybackSettings());
        Assert.Equal(["VideoProvider:ReadOnlyApiKey"], options.GetMissingWebhookSettings());
        Assert.Equal(5, options.GetAllMissingSettings().Count);
    }

    [Fact]
    public void FullyConfigured_ReportsNothingMissing()
    {
        var options = FullyConfigured();

        Assert.Empty(options.GetMissingApiSettings());
        Assert.Empty(options.GetMissingPlaybackSettings());
        Assert.Empty(options.GetMissingWebhookSettings());
        Assert.Empty(options.GetAllMissingSettings());
    }

    [Fact]
    public void CdnHostnameAndTokenKey_AreOptionalForApiCalls()
    {
        var options = FullyConfigured();
        options.CdnHostname = string.Empty;
        options.TokenAuthenticationKey = string.Empty;

        // Management-API calls (create/status/delete/upload) do not need the playback settings.
        Assert.Empty(options.GetMissingApiSettings());
        Assert.Equal(2, options.GetMissingPlaybackSettings().Count);
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("000000")]
    [InlineData("CHANGE_ME_DEV_ONLY_library")]
    public void LibraryId_EmptyUnsetOrPlaceholder_IsMissing(string libraryId)
    {
        var options = FullyConfigured();
        options.LibraryId = libraryId;

        Assert.Equal(["VideoProvider:LibraryId"], options.GetMissingApiSettings());
    }

    [Theory]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("CHANGE_ME_DEV_ONLY_bunny_api_key")]
    [InlineData("change_me")]
    public void ApiKey_EmptyOrPlaceholder_IsMissing(string apiKey)
    {
        var options = FullyConfigured();
        options.ApiKey = apiKey;

        Assert.Equal(["VideoProvider:ApiKey"], options.GetMissingApiSettings());
    }

    [Theory]
    [InlineData("CHANGE_ME_DEV_ONLY_bunny_readonly_api_key")]
    [InlineData("a-placeholder-value")]
    public void ReadOnlyApiKey_Placeholder_IsMissingForWebhooks(string key)
    {
        // The Bunny webhook HMAC key: a committed placeholder is publicly known, so it must count as unset.
        var options = FullyConfigured();
        options.ReadOnlyApiKey = key;

        Assert.Equal(["VideoProvider:ReadOnlyApiKey"], options.GetMissingWebhookSettings());
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData("", true)]
    [InlineData("   ", true)]
    [InlineData("CHANGE_ME", true)]
    [InlineData("xx-CHANGE_ME-xx", true)]
    [InlineData("my-placeholder.b-cdn.net", true)]
    [InlineData("my-pull-zone.b-cdn.net", false)]
    [InlineData("12345", false)]
    public void IsPlaceholder_ClassifiesValues(string? value, bool expected)
    {
        Assert.Equal(expected, VideoProviderOptions.IsPlaceholder(value));
    }

    [Fact]
    public void MissingSettingNames_NeverContainSecretValues()
    {
        var options = FullyConfigured();
        options.ApiKey = "CHANGE_ME_secret-looking-value";

        var missing = Assert.Single(options.GetMissingApiSettings());
        Assert.DoesNotContain("secret-looking-value", missing);
    }

    [Fact]
    public void SectionName_IsVideoProvider()
    {
        Assert.Equal("VideoProvider", VideoProviderOptions.SectionName);
    }
}
