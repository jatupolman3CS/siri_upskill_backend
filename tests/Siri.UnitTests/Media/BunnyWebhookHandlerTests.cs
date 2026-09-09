using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Siri.Integrations.Video;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Integrations.Video.Bunny;
using Siri.Modules.Media.Application;
using Siri.Modules.Media.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Media;

public sealed class BunnyWebhookHandlerTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private sealed class FakeMediaAssetRepository : IMediaAssetRepository
    {
        public readonly Dictionary<Guid, MEDIA_ASSET> Assets = [];

        public Task<MEDIA_ASSET?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Assets.TryGetValue(id, out var asset) ? asset : null);

        public Task<MEDIA_ASSET?> GetByProviderAssetIdAsync(string providerAssetId, CancellationToken cancellationToken) =>
            Task.FromResult(Assets.Values.FirstOrDefault(a => a.PROVIDER_ASSET_ID == providerAssetId));

        public Task<(IReadOnlyList<MEDIA_ASSET> Items, int TotalCount)> GetPagedByUploaderAsync(
            Guid uploadedByUserId, int page, int pageSize, CancellationToken cancellationToken)
        {
            var items = Assets.Values.Where(a => a.UPLOADED_BY_USER_ID == uploadedByUserId).ToList();
            return Task.FromResult(((IReadOnlyList<MEDIA_ASSET>)items, items.Count));
        }

        public void Add(MEDIA_ASSET mediaAsset) => Assets[mediaAsset.MEDIA_ASSET_ID] = mediaAsset;

        public void Remove(MEDIA_ASSET mediaAsset) => Assets.Remove(mediaAsset.MEDIA_ASSET_ID);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private const string Secret = "test-read-only-key";
    private const string VideoId = "657bb740-a71b-4529-a012-528021c31a92";
    private readonly FakeMediaAssetRepository _repo = new();
    private readonly FakeProvider _provider = new();
    private readonly FakeClock _clock = new(DateTime.UtcNow);
    private readonly MEDIA_ASSET _asset = MEDIA_ASSET.Create("BunnyStream", VideoId, Guid.NewGuid(), true);

    private BunnyWebhookHandler Handler()
    {
        _repo.Add(_asset);
        return new(_repo, _provider, Options.Create(new VideoProviderOptions
        {
            LibraryId = "12345", ReadOnlyApiKey = Secret,
        }), _clock, NullLogger<BunnyWebhookHandler>.Instance);
    }

    private static byte[] Body(int status = 3, long library = 12345) =>
        Encoding.UTF8.GetBytes($$"""{ "VideoLibraryId": {{library}}, "VideoGuid": "{{VideoId}}", "Status": {{status}}, "Duration": 9999 }""");
    private static string Sign(byte[] body) => Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes(Secret), body));
    private Task<Result> Send(byte[] body) => Handler().HandleSignedWebhookAsync(body, "v1", "hmac-sha256", Sign(body), default);

    [Theory]
    [InlineData(null, "hmac-sha256", "valid")]
    [InlineData("v2", "hmac-sha256", "valid")]
    [InlineData("v1", "sha256", "valid")]
    [InlineData("v1", "hmac-sha256", null)]
    [InlineData("v1", "hmac-sha256", "invalid")]
    [InlineData("v1", "hmac-sha256", "wrong-key")]
    public async Task Signature_InvalidHeaders_RejectBeforeMutation(string? version, string? algorithm, string? signature)
    {
        var body = Body();
        signature = signature == "valid" ? Sign(body) : signature == "wrong-key" ? new string('0', 64) : signature;
        var result = await Handler().HandleSignedWebhookAsync(body, version, algorithm, signature, default);
        Assert.Equal(BunnyWebhookHandler.InvalidSignature, result.Error.Code);
        Assert.Equal(MediaAssetStatus.Uploading, _asset.STATUS);
        Assert.Equal(0, _provider.Calls);
    }

    [Fact]
    public async Task Signature_WhitespaceChanged_RejectsTampering()
    {
        var body = Body();
        var changed = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(body).Replace(" ", ""));
        var result = await Handler().HandleSignedWebhookAsync(changed, "v1", "hmac-sha256", Sign(body), default);
        Assert.Equal(BunnyWebhookHandler.InvalidSignature, result.Error.Code);
        Assert.Equal(0, _provider.Calls);
    }

    [Fact]
    public async Task Signature_WrongLibrary_RejectsEvenWhenSigned()
    {
        var result = await Send(Body(library: 987));
        Assert.Equal("forbidden", result.Error.Code);
        Assert.Equal(0, _provider.Calls);
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{")]
    [InlineData("{}")]
    public async Task Payload_InvalidJson_ReturnsValidation(string json)
    {
        var result = await Send(Encoding.UTF8.GetBytes(json));
        Assert.Equal("validation", result.Error.Code);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(6)]
    [InlineData(8)]
    public async Task Callback_UsesCurrentProviderStateAndDuration_ThenIgnoresReplay(int callbackStatus)
    {
        _provider.Status = VideoProcessingStatus.Ready;
        Assert.True((await Send(Body(callbackStatus))).IsSuccess);
        Assert.Equal(MediaAssetStatus.Ready, _asset.STATUS);
        Assert.Equal(120, _asset.DURATION_SECONDS); // Never trust callback Duration=9999.
        Assert.True((await Send(Body(5))).IsSuccess);
        Assert.Equal(MediaAssetStatus.Ready, _asset.STATUS);
        Assert.Equal(1, _provider.Calls);
    }

    [Fact]
    public async Task UploadStarted_ProviderUploading_DoesNotMarkFailed()
    {
        _provider.Status = VideoProcessingStatus.Uploading;
        Assert.True((await Send(Body(6))).IsSuccess);
        Assert.Equal(MediaAssetStatus.Uploading, _asset.STATUS);
    }

    [Theory]
    [InlineData(VideoProcessingStatus.Processing, MediaAssetStatus.Processing)]
    [InlineData(VideoProcessingStatus.Failed, MediaAssetStatus.Failed)]
    public async Task Callback_ProviderTransition_UpdatesAsset(VideoProcessingStatus status, MediaAssetStatus expected)
    {
        _provider.Status = status;
        Assert.True((await Send(Body())).IsSuccess);
        Assert.Equal(expected, _asset.STATUS);
    }

    [Theory]
    [InlineData(false, 503)]
    [InlineData(true, 413)]
    public async Task HttpBinding_ProviderFailureOrOversizedBody_ReturnsExpectedStatus(bool oversized, int expected)
    {
        _provider.Fail = true;
        var body = oversized ? new byte[BunnyWebhookHandler.MaximumBodyBytes + 1] : Body();
        var context = new DefaultHttpContext();
        context.Request.Body = new MemoryStream(body);
        context.Request.Headers["X-BunnyStream-Signature-Version"] = "v1";
        context.Request.Headers["X-BunnyStream-Signature-Algorithm"] = "hmac-sha256";
        context.Request.Headers["X-BunnyStream-Signature"] = Sign(body);
        var result = await BunnyWebhookRequest.HandleAsync(context, Handler(), default);
        Assert.Equal(expected, ((IStatusCodeHttpResult)result).StatusCode);
        Assert.Equal(MediaAssetStatus.Uploading, _asset.STATUS);
    }

    private sealed class FakeProvider : IVideoProvider
    {
        public VideoProcessingStatus Status = VideoProcessingStatus.Ready;
        public bool Fail;
        public int Calls;
        public Task<Result<VideoStatus>> GetStatusAsync(string id, CancellationToken cancellationToken)
        {
            Calls++;
            return Task.FromResult(Fail
                ? Result.Failure<VideoStatus>(DomainError.Validation("Provider unavailable"))
                : Result.Success(new VideoStatus(id, Status, TimeSpan.FromSeconds(120))));
        }
        public Task<Result<VideoAsset>> CreateVideoAsync(string title, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<VideoUploadUrl>> GetUploadUrlAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result> DeleteVideoAsync(string id, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<Result<SignedPlaybackUrl>> GetSignedPlaybackUrlAsync(string id, TimeSpan ttl, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}