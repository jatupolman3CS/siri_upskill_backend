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

    [Fact]
    public async Task HandleWebhookAsync_WhenFinishedStatus_MarksAssetReady()
    {
        var repo = new FakeMediaAssetRepository();
        var options = Options.Create(new VideoProviderOptions
        {
            LibraryId = "12345",
            ApiKey = "test-api-key",
            ReadOnlyApiKey = "test-ro-key",
            PullZone = "test-pull-zone",
            TokenAuthenticationKey = "test-token-key",
            CdnHostname = "video.siriupskill.com",
        });
        var clock = new FakeClock(DateTime.UtcNow);
        var handler = new BunnyWebhookHandler(repo, options, clock, NullLogger<BunnyWebhookHandler>.Instance);

        var asset = MEDIA_ASSET.Create("BunnyStream", "bunny-vid-100", Guid.NewGuid(), true);
        repo.Add(asset);

        var payload = new BunnyWebhookPayload(
            VideoLibraryId: 12345,
            VideoGuid: "bunny-vid-100",
            Status: BunnyWebhookHandler.StatusFinished,
            ThumbnailFileName: "thumb.jpg",
            Duration: 300);

        var result = await handler.HandleWebhookAsync(payload, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MediaAssetStatus.Ready, asset.STATUS);
        Assert.Equal("bunny-vid-100", asset.PLAYBACK_ID);
        Assert.Equal(300, asset.DURATION_SECONDS);
        Assert.Equal("https://video.siriupskill.com/bunny-vid-100/thumb.jpg", asset.THUMBNAIL_URL);
    }

    [Fact]
    public async Task HandleWebhookAsync_WhenErrorStatus_MarksAssetFailed()
    {
        var repo = new FakeMediaAssetRepository();
        var options = Options.Create(new VideoProviderOptions
        {
            LibraryId = "12345",
            ApiKey = "test-api-key",
            ReadOnlyApiKey = "test-ro-key",
            PullZone = "test-pull-zone",
            TokenAuthenticationKey = "test-token-key",
            CdnHostname = "video.siriupskill.com",
        });
        var clock = new FakeClock(DateTime.UtcNow);
        var handler = new BunnyWebhookHandler(repo, options, clock, NullLogger<BunnyWebhookHandler>.Instance);

        var asset = MEDIA_ASSET.Create("BunnyStream", "bunny-vid-200", Guid.NewGuid(), true);
        repo.Add(asset);

        var payload = new BunnyWebhookPayload(
            VideoLibraryId: 12345,
            VideoGuid: "bunny-vid-200",
            Status: BunnyWebhookHandler.StatusError,
            ThumbnailFileName: null,
            Duration: null);

        var result = await handler.HandleWebhookAsync(payload, null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MediaAssetStatus.Failed, asset.STATUS);
        Assert.Contains("Transcoding failed", asset.ERROR_MESSAGE);
    }
}
