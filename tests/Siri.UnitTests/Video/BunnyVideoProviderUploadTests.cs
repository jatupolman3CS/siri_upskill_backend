using System.Net;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Integrations.Video;
using Siri.Integrations.Video.Bunny;

namespace Siri.UnitTests.Video;

/// <summary>P11-13: <c>BunnyVideoProvider.UploadVideoAsync</c> — the server-side PUT of a recording.</summary>
public sealed class BunnyVideoProviderUploadTests
{
    private const string VideoId = "448944e4-c1bd-4f61-a8b1-3e10e469aa69";

    private static VideoProviderOptions ValidOptions() => new()
    {
        LibraryId = "12345",
        ApiKey = "test-api-key-abc123",
        ReadOnlyApiKey = "test-readonly-key",
        PullZone = "zone",
        CdnHostname = "zone.b-cdn.net",
        TokenAuthenticationKey = "token-key",
    };

    private static BunnyVideoProvider Create(RecordingHandler handler, VideoProviderOptions? options = null) =>
        new(handler.Factory, Options.Create(options ?? ValidOptions()), NullLogger<BunnyVideoProvider>.Instance);

    [Fact]
    public async Task UploadVideoAsync_SendsAPutOfTheRawBytesWithTheAccessKeyAndLength()
    {
        var bytes = new byte[10_000];
        new Random(7).NextBytes(bytes);
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await Create(handler).UploadVideoAsync(VideoId, new MemoryStream(bytes), bytes.Length, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var request = Assert.Single(handler.Requests);
        Assert.Equal(HttpMethod.Put, request.Method);
        Assert.Equal($"https://video.bunnycdn.com/library/12345/videos/{VideoId}", request.Uri.AbsoluteUri);
        Assert.Equal("test-api-key-abc123", request.AccessKey);
        Assert.Equal(bytes.Length, request.ContentLength);
        Assert.Equal("application/octet-stream", request.ContentType);
        Assert.Equal(bytes, request.Body);
        Assert.Equal(BunnyVideoProvider.UploadHttpClientName, Assert.Single(handler.ClientNames));
    }

    [Fact]
    public async Task UploadVideoAsync_UnknownLength_SendsNoLengthHeaderForAForwardOnlyStream()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await Create(handler).UploadVideoAsync(VideoId, new ForwardOnlyStream(new byte[100]), contentLength: null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(Assert.Single(handler.Requests).ContentLength);
    }

    [Fact]
    public async Task UploadVideoAsync_Unavailable_IsNotRetried()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable));

        var result = await Create(handler).UploadVideoAsync(VideoId, new MemoryStream(new byte[10]), 10, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("video.provider_error", result.Error.Code);
        // A consumed stream cannot be replayed, so exactly one attempt - the caller restarts the whole transfer.
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized, "video.unauthorized")]
    [InlineData(HttpStatusCode.Forbidden, "video.forbidden")]
    [InlineData(HttpStatusCode.NotFound, "not_found")]
    [InlineData(HttpStatusCode.TooManyRequests, "video.provider_error")]
    [InlineData(HttpStatusCode.BadRequest, "video.provider_error")]
    [InlineData(HttpStatusCode.InternalServerError, "video.provider_error")]
    public async Task UploadVideoAsync_FailureStatuses_AreMappedLikeTheOtherOperations(HttpStatusCode status, string expectedCode)
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(status) { Content = new StringContent("""{"Message":"nope"}""") });

        var result = await Create(handler).UploadVideoAsync(VideoId, new MemoryStream(new byte[10]), 10, CancellationToken.None);

        Assert.Equal(expectedCode, result.Error.Code);
        Assert.Single(handler.Requests);
    }

    [Theory]
    [InlineData("", "key")]
    [InlineData("12345", "")]
    [InlineData("000000", "key")]
    [InlineData("12345", "CHANGE_ME")]
    public async Task UploadVideoAsync_NotConfigured_FailsWithoutCallingBunny(string libraryId, string apiKey)
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));
        var options = ValidOptions();
        options.LibraryId = libraryId;
        options.ApiKey = apiKey;

        var result = await Create(handler, options).UploadVideoAsync(VideoId, new MemoryStream(new byte[1]), 1, CancellationToken.None);

        Assert.Equal(VideoProviderErrors.ProviderNotConfiguredCode, result.Error.Code);
        Assert.Empty(handler.Requests);
    }

    [Theory]
    [InlineData("")]
    [InlineData("../library/1/videos")]
    [InlineData("a/b")]
    [InlineData("id?x=1")]
    public async Task UploadVideoAsync_UnsafeVideoId_IsRefusedWithoutCallingBunny(string videoId)
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        var result = await Create(handler).UploadVideoAsync(videoId, new MemoryStream(new byte[1]), 1, CancellationToken.None);

        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task UploadVideoAsync_NetworkFailure_IsAFailedResultNotAnException()
    {
        using var handler = new RecordingHandler(_ => throw new HttpRequestException("connection reset"));

        var result = await Create(handler).UploadVideoAsync(VideoId, new MemoryStream(new byte[10]), 10, CancellationToken.None);

        Assert.Equal("video.provider_error", result.Error.Code);
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task UploadVideoAsync_CallerCancellation_Propagates()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(
            () => Create(handler).UploadVideoAsync(VideoId, new MemoryStream(new byte[10]), 10, cts.Token));
    }

    [Fact]
    public async Task UploadVideoAsync_NullStream_Throws()
    {
        using var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

        await Assert.ThrowsAsync<ArgumentNullException>(() => Create(handler).UploadVideoAsync(VideoId, null!, 1, CancellationToken.None));
    }

    private sealed record RecordedUpload(HttpMethod Method, Uri Uri, string? AccessKey, long? ContentLength, string? ContentType, byte[] Body);

    private sealed class RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
    {
        public List<RecordedUpload> Requests { get; } = [];

        public List<string> ClientNames { get; } = [];

        public IHttpClientFactory Factory => new NamedFactory(this);

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // A real handler never starts a send for a token that is already cancelled.
            cancellationToken.ThrowIfCancellationRequested();
            // Read the length header BEFORE buffering the body: buffering makes a stream's length known.
            var contentLength = request.Content?.Headers.ContentLength;
            var body = request.Content is null ? [] : await request.Content.ReadAsByteArrayAsync(cancellationToken);
            Requests.Add(new RecordedUpload(
                request.Method,
                request.RequestUri!,
                request.Headers.TryGetValues("AccessKey", out var key) ? key.Single() : null,
                contentLength,
                request.Content?.Headers.ContentType?.MediaType,
                body));
            return respond(request);
        }

        private sealed class NamedFactory(RecordingHandler owner) : IHttpClientFactory
        {
            public HttpClient CreateClient(string name)
            {
                owner.ClientNames.Add(name);
                return new HttpClient(owner, disposeHandler: false);
            }
        }
    }

    /// <summary>A readable stream that cannot seek, so <see cref="StreamContent"/> cannot know its length.</summary>
    private sealed class ForwardOnlyStream(byte[] data) : Stream
    {
        private readonly MemoryStream _inner = new(data);

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => false;

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);

        public override void Flush()
        {
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        public override void SetLength(long value) => throw new NotSupportedException();

        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
