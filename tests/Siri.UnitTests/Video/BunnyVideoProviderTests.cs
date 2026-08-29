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

        // URL must point to the playlist file
        Assert.StartsWith("https://my-pull-zone.b-cdn.net/12345/video-guid-123/playlist.m3u8?token=HS256-", url);
        // Token must use HMAC-SHA256 HS256 prefix (not plain hex)
        Assert.Contains("HS256-", url);
        Assert.Contains("&expires=1700000000", url);
        // Directory-level token_path must be present so HLS segments are also authorized
        Assert.Contains("token_path=", url);
        Assert.Contains(Uri.EscapeDataString("/12345/video-guid-123/"), url);
    }

    [Fact]
    public void GenerateSignedPlaybackUrl_DifferentVideos_ProduceDifferentTokens()
    {
        var provider = CreateProvider();
        var url1 = provider.GenerateSignedPlaybackUrl("video-1", 1700000000);
        var url2 = provider.GenerateSignedPlaybackUrl("video-2", 1700000000);

        Assert.NotEqual(url1, url2);
    }

    [Fact]
    public void GenerateSignedPlaybackUrl_DifferentExpiry_ProduceDifferentTokens()
    {
        var provider = CreateProvider();
        var url1 = provider.GenerateSignedPlaybackUrl("video-1", 1700000000);
        var url2 = provider.GenerateSignedPlaybackUrl("video-1", 1700000001);

        Assert.NotEqual(url1, url2);
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
