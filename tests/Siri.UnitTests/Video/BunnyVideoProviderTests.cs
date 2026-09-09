using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Integrations.Video;
using Siri.Integrations.Video.Bunny;

namespace Siri.UnitTests.Video;

public class BunnyVideoProviderTests
{
    private static VideoProviderOptions CreateValidOptions() => new()
    {
        LibraryId = "12345",
        ApiKey = "test-api-key-abc123",
        ReadOnlyApiKey = "test-readonly-key",
        PullZone = "my-pull-zone",
        CdnHostname = "my-pull-zone.b-cdn.net",
        TokenAuthenticationKey = "test-token-auth-key-secret",
    };

    private static BunnyVideoProvider CreateProvider(
        VideoProviderOptions? options = null,
        HttpMessageHandler? handler = null)
    {
        var opts = options ?? CreateValidOptions();

        var factory = new TestHttpClientFactory(handler ?? new FakeHandler(HttpStatusCode.OK, "{}"));

        return new BunnyVideoProvider(
            factory,
            Options.Create(opts),
            NullLogger<BunnyVideoProvider>.Instance);
    }

    // -----------------------------------------------------------------------
    // Signed playback URL tests (pure function, no HTTP)
    // -----------------------------------------------------------------------

    [Fact]
    public void GenerateSignedPlaybackUrl_ProducesCorrectFormat()
    {
        var provider = CreateProvider();
        var url = provider.GenerateSignedPlaybackUrl("video-guid-123", 1700000000);

        // Stream CDN paths start with the video ID, not the management API's library ID.
        Assert.StartsWith("https://my-pull-zone.b-cdn.net/bcdn_token=HS256-", url);
        Assert.EndsWith("/video-guid-123/playlist.m3u8", url);
        Assert.DoesNotContain("/12345/", url);
        Assert.Contains("&expires=1700000000", url);
        Assert.Contains("&token_path=%2Fvideo-guid-123%2F", url);
        // Query tokens disappear when an HLS player resolves relative playlist/segment paths.
        Assert.Empty(new Uri(url).Query);
    }

    [Theory]
    [InlineData("video-guid-123", "HS256-y9x-t2pGURWmysoI6cUZQO75QH98YVoaD6VrGGGmPZg")]
    [InlineData("448944e4-c1bd-4f61-a8b1-3e10e469aa69", "HS256-o9PFi5REmVeCbTaZy0PjwBacc93vWGv47Rwx2hjevlU")]
    public void GenerateSignedPlaybackUrl_MatchesKnownHmacSignature(string videoId, string expectedToken)
    {
        // Independent Node.js crypto vectors for the documented Bunny signing message:
        // /{videoId}/1700000000token_path=/{videoId}/
        // Key: test-token-auth-key-secret (HMAC key only; never part of the message).
        // https://bunny.net/docs/cdn/security/token-authentication/advanced
        var provider = CreateProvider();

        var url = provider.GenerateSignedPlaybackUrl(videoId, 1700000000);

        Assert.Equal(
            $"https://my-pull-zone.b-cdn.net/bcdn_token={expectedToken}&expires=1700000000&token_path=%2F{videoId}%2F/{videoId}/playlist.m3u8",
            url);
    }

    [Theory]
    [InlineData("720p/video.m3u8", "video0.ts", "720p/video0.ts")]
    [InlineData("1080p/video.m3u8", "../audio/segment-001.aac", "audio/segment-001.aac")]
    public void GenerateSignedPlaybackUrl_RelativeHlsRequestsRetainAuthenticationAndScope(
        string relativePlaylist,
        string relativeSegment,
        string expectedSegmentPath)
    {
        var masterPlaylist = new Uri(CreateProvider().GenerateSignedPlaybackUrl("video-guid-123", 1700000000));

        var childPlaylist = new Uri(masterPlaylist, relativePlaylist);
        var segment = new Uri(childPlaylist, relativeSegment);

        foreach (var resource in new[] { childPlaylist, segment })
        {
            Assert.Equal(masterPlaylist.Segments[1], resource.Segments[1]);
            Assert.StartsWith("bcdn_token=HS256-", resource.Segments[1]);
            Assert.Contains("&expires=1700000000", resource.Segments[1]);
            Assert.Contains("&token_path=%2Fvideo-guid-123%2F", resource.Segments[1]);
            Assert.Equal("video-guid-123/", resource.Segments[2]);
            Assert.Empty(resource.Query);
        }

        Assert.EndsWith($"/video-guid-123/{relativePlaylist}", childPlaylist.AbsoluteUri);
        Assert.EndsWith($"/video-guid-123/{expectedSegmentPath}", segment.AbsoluteUri);
    }

    [Fact]
    public void GenerateSignedPlaybackUrl_LibraryIdDoesNotAlterCdnPathOrToken()
    {
        var otherOptions = CreateValidOptions();
        otherOptions.LibraryId = "67890";

        var firstUrl = CreateProvider().GenerateSignedPlaybackUrl("video-guid-123", 1700000000);
        var otherUrl = CreateProvider(otherOptions).GenerateSignedPlaybackUrl("video-guid-123", 1700000000);

        Assert.Equal(firstUrl, otherUrl);
    }

    [Fact]
    public void GenerateSignedPlaybackUrl_DifferentVideos_ProduceDifferentTokens()
    {
        var provider = CreateProvider();
        var url1 = provider.GenerateSignedPlaybackUrl("video-1", 1700000000);
        var url2 = provider.GenerateSignedPlaybackUrl("video-2", 1700000000);

        Assert.NotEqual(new Uri(url1).Segments[1].Split('&')[0], new Uri(url2).Segments[1].Split('&')[0]);
    }

    [Fact]
    public void GenerateSignedPlaybackUrl_DifferentExpiry_ProduceDifferentTokens()
    {
        var provider = CreateProvider();
        var url1 = provider.GenerateSignedPlaybackUrl("video-1", 1700000000);
        var url2 = provider.GenerateSignedPlaybackUrl("video-1", 1700000001);

        Assert.NotEqual(new Uri(url1).Segments[1].Split('&')[0], new Uri(url2).Segments[1].Split('&')[0]);
    }

    [Fact]
    public void GenerateSignedPlaybackUrl_SameInputs_ProduceSameResult()
    {
        var provider = CreateProvider();
        var url1 = provider.GenerateSignedPlaybackUrl("video-1", 1700000000);
        var url2 = provider.GenerateSignedPlaybackUrl("video-1", 1700000000);

        Assert.Equal(url1, url2);
    }

    // -----------------------------------------------------------------------
    // GetSignedPlaybackUrlAsync — CdnHostname / TokenAuthenticationKey checks
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetSignedPlaybackUrlAsync_NoCdnHostname_ReturnsFailure()
    {
        var opts = CreateValidOptions();
        opts.CdnHostname = "";
        var provider = CreateProvider(opts);

        var result = await provider.GetSignedPlaybackUrlAsync("video-1", TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("video.cdn_not_configured", result.Error.Code);
    }

    [Fact]
    public async Task GetSignedPlaybackUrlAsync_NoTokenAuthKey_ReturnsFailure()
    {
        var opts = CreateValidOptions();
        opts.TokenAuthenticationKey = "";
        var provider = CreateProvider(opts);

        var result = await provider.GetSignedPlaybackUrlAsync("video-1", TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("video.token_auth_not_configured", result.Error.Code);
    }

    [Fact]
    public async Task GetSignedPlaybackUrlAsync_ValidConfig_ReturnsSuccess()
    {
        var provider = CreateProvider();

        var result = await provider.GetSignedPlaybackUrlAsync("video-1", TimeSpan.FromMinutes(5), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("my-pull-zone.b-cdn.net", result.Value.ManifestUrl);
        Assert.True(result.Value.ExpiresAtUtc > DateTime.UtcNow);
    }

    // -----------------------------------------------------------------------
    // GetUploadUrlAsync (pure computation, no HTTP)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetUploadUrlAsync_ReturnsValidUploadUrl()
    {
        var provider = CreateProvider();

        var result = await provider.GetUploadUrlAsync("video-guid-123", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("tusupload", result.Value.UploadUrl);
        Assert.Contains("AuthorizationSignature=", result.Value.UploadUrl);
        Assert.Contains("AuthorizationExpire=", result.Value.UploadUrl);
        Assert.Contains("LibraryId=12345", result.Value.UploadUrl);
        Assert.Contains("VideoId=video-guid-123", result.Value.UploadUrl);
        Assert.True(result.Value.ExpiresAtUtc > DateTime.UtcNow);
    }

    // -----------------------------------------------------------------------
    // CreateVideoAsync — HTTP interaction tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task CreateVideoAsync_SuccessResponse_ReturnsVideoAsset()
    {
        var responseJson = JsonSerializer.Serialize(new { guid = "new-video-id", title = "My Video" });
        var handler = new FakeHandler(HttpStatusCode.OK, responseJson);
        var provider = CreateProvider(handler: handler);

        var result = await provider.CreateVideoAsync("My Video", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("new-video-id", result.Value.ProviderVideoId);
        Assert.Equal("My Video", result.Value.Title);
    }

    [Fact]
    public async Task CreateVideoAsync_Unauthorized_ReturnsFailure()
    {
        var handler = new FakeHandler(HttpStatusCode.Unauthorized, "");
        var provider = CreateProvider(handler: handler);

        var result = await provider.CreateVideoAsync("My Video", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("video.unauthorized", result.Error.Code);
    }

    // -----------------------------------------------------------------------
    // GetStatusAsync — HTTP interaction tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetStatusAsync_Finished_ReturnsReady()
    {
        var responseJson = JsonSerializer.Serialize(new { guid = "vid-1", status = 4, length = 120.5 });
        var handler = new FakeHandler(HttpStatusCode.OK, responseJson);
        var provider = CreateProvider(handler: handler);

        var result = await provider.GetStatusAsync("vid-1", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(VideoProcessingStatus.Ready, result.Value.Status);
        Assert.NotNull(result.Value.Duration);
        Assert.Equal(120.5, result.Value.Duration!.Value.TotalSeconds, precision: 1);
    }

    [Fact]
    public async Task GetStatusAsync_Processing_ReturnsProcessing()
    {
        var responseJson = JsonSerializer.Serialize(new { guid = "vid-1", status = 3, length = 0.0 });
        var handler = new FakeHandler(HttpStatusCode.OK, responseJson);
        var provider = CreateProvider(handler: handler);

        var result = await provider.GetStatusAsync("vid-1", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(VideoProcessingStatus.Processing, result.Value.Status);
    }

    [Fact]
    public async Task GetStatusAsync_NotFound_ReturnsFailure()
    {
        var handler = new FakeHandler(HttpStatusCode.NotFound, "");
        var provider = CreateProvider(handler: handler);

        var result = await provider.GetStatusAsync("nonexistent", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    // -----------------------------------------------------------------------
    // DeleteVideoAsync — HTTP interaction tests
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DeleteVideoAsync_Success_ReturnsSuccess()
    {
        var handler = new FakeHandler(HttpStatusCode.OK, "");
        var provider = CreateProvider(handler: handler);

        var result = await provider.DeleteVideoAsync("vid-1", CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteVideoAsync_NotFound_StillReturnsSuccess()
    {
        // Deleting a video that doesn't exist is acceptable (idempotent).
        var handler = new FakeHandler(HttpStatusCode.NotFound, "");
        var provider = CreateProvider(handler: handler);

        var result = await provider.DeleteVideoAsync("nonexistent", CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task DeleteVideoAsync_ServerError_ReturnsFailure()
    {
        var handler = new FakeHandler(HttpStatusCode.InternalServerError, "server error");
        var provider = CreateProvider(handler: handler);

        var result = await provider.DeleteVideoAsync("vid-1", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("video.provider_error", result.Error.Code);
    }

    // -----------------------------------------------------------------------
    // Test helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// A minimal <see cref="IHttpClientFactory"/> for unit tests that returns a client backed by the
    /// provided <see cref="HttpMessageHandler"/>.
    /// </summary>
    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    /// <summary>
    /// A fake <see cref="HttpMessageHandler"/> that always returns a fixed status code and body.
    /// Good enough for unit tests that verify error mapping and result construction.
    /// </summary>
    private sealed class FakeHandler(HttpStatusCode statusCode, string responseBody) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(responseBody, System.Text.Encoding.UTF8, "application/json"),
            });
    }
}
