using Siri.Integrations.Video;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Media.Application;
using Siri.Modules.Media.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Media;

public sealed class PlaybackSessionServiceTests
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

    private sealed class FakePlaybackSessionRepository : IPlaybackSessionRepository
    {
        public readonly List<PLAYBACK_SESSION> Sessions = [];

        public Task<PLAYBACK_SESSION?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Sessions.FirstOrDefault(s => s.PLAYBACK_SESSION_ID == id));

        public Task<(IReadOnlyList<PLAYBACK_SESSION> Items, int TotalCount)> GetPagedByUserIdAsync(
            Guid userId, int page, int pageSize, CancellationToken cancellationToken)
        {
            var items = Sessions.Where(s => s.USER_ID == userId).ToList();
            return Task.FromResult(((IReadOnlyList<PLAYBACK_SESSION>)items, items.Count));
        }

        public Task<IReadOnlyList<PlaybackUserActivity>> GetUserActivitySinceAsync(
            DateTime sinceUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PlaybackUserActivity>>([]);

        public void Add(PLAYBACK_SESSION playbackSession) => Sessions.Add(playbackSession);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeVideoProvider : IVideoProvider
    {
        public Task<Result<VideoAsset>> CreateVideoAsync(string title, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new VideoAsset("vid-1", title)));

        public Task<Result<VideoUploadUrl>> GetUploadUrlAsync(string providerVideoId, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(
                new VideoUploadUrl("https://upload.bunny.net/tus/vid-1", DateTime.UtcNow.AddMinutes(30))));

        public Task<Result<VideoStatus>> GetStatusAsync(string providerVideoId, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(
                new VideoStatus(providerVideoId, VideoProcessingStatus.Ready, TimeSpan.FromMinutes(2))));

        public Task<Result> DeleteVideoAsync(string providerVideoId, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success());

        public Task<Result<SignedPlaybackUrl>> GetSignedPlaybackUrlAsync(
            string providerVideoId,
            TimeSpan timeToLive,
            CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(
                new SignedPlaybackUrl("https://video.siriupskill.com/vid-1/playlist.m3u8?token=xyz", DateTime.UtcNow.Add(timeToLive))));
    }

    private sealed class FakeLearningAccessContract : ILearningAccessContract
    {
        public bool AccessGranted { get; set; } = true;

        public Task<bool> CanUserAccessEpisodeAsync(Guid userId, Guid episodeId, CancellationToken cancellationToken) =>
            Task.FromResult(AccessGranted);

        public Task<bool> HasActiveEnrollmentAsync(Guid userId, Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult(AccessGranted);

        public Task<Result> EnrollUserAsync(Guid userId, Guid courseId, Guid? orderId, string source, DateTime? expiresAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success());
    }

    private sealed class FakeCatalogPriceContract : ICatalogPriceContract
    {
        public readonly Dictionary<Guid, Guid?> EpisodeMediaAssets = [];

        public Task<IReadOnlyDictionary<Guid, CoursePriceInfo>> GetPublishedCoursePricesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, CoursePriceInfo>>(new Dictionary<Guid, CoursePriceInfo>());

        public Task<bool> IsEpisodeFreePreviewAsync(Guid episodeId, CancellationToken cancellationToken) =>
            Task.FromResult(true);

        public Task<Guid?> GetCourseIdForEpisodeAsync(Guid episodeId, CancellationToken cancellationToken) =>
            Task.FromResult<Guid?>(Guid.NewGuid());

        public Task<Guid?> GetMediaAssetIdForEpisodeAsync(Guid episodeId, CancellationToken cancellationToken) =>
            Task.FromResult(EpisodeMediaAssets.TryGetValue(episodeId, out var mediaAssetId) ? mediaAssetId : null);

        public Task<bool> IsInstructorOwnerOfEpisodeAsync(Guid episodeId, Guid instructorUserId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<bool> IsInstructorOwnerOfCourseAsync(Guid courseId, Guid instructorUserId, CancellationToken cancellationToken) =>
            Task.FromResult(false);

        public Task<int> GetPendingReviewsCountAsync(CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<IReadOnlyDictionary<Guid, string>> GetCourseTitlesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

        public Task<IReadOnlyDictionary<Guid, decimal>> GetInstructorRevenueSharePercentsAsync(IEnumerable<Guid> instructorIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>());
    }

    [Fact]
    public async Task CreateAsync_WhenAssetIsReady_IssuesSignedUrlAndWatermark()
    {
        var sessionRepo = new FakePlaybackSessionRepository();
        var assetRepo = new FakeMediaAssetRepository();
        var provider = new FakeVideoProvider();
        var learning = new FakeLearningAccessContract();
        var catalog = new FakeCatalogPriceContract();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var service = new PlaybackSessionService(sessionRepo, assetRepo, provider, learning, catalog, clock);

        var userId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();

        var asset = MEDIA_ASSET.Create("BunnyStream", "vid-1", Guid.NewGuid(), true);
        asset.MarkProcessing();
        asset.MarkReady("playback-1", 300, "https://cdn/thumb.jpg", clock);
        assetRepo.Add(asset);

        var command = new CreatePlaybackSessionCommand(episodeId, asset.MEDIA_ASSET_ID, "device-chrome");
        var result = await service.CreateAsync(userId, sessionId, "203.0.113.1", command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("playlist.m3u8", result.Value.ManifestUrl);
        Assert.Contains(userId.ToString(), result.Value.WatermarkPayload);
        Assert.Single(sessionRepo.Sessions);

        var recorded = sessionRepo.Sessions[0];
        Assert.Equal(userId, recorded.USER_ID);
        Assert.Equal(episodeId, recorded.EPISODE_ID);
        Assert.Equal(sessionId, recorded.SESSION_ID);
        Assert.Equal("device-chrome", recorded.DEVICE_ID);
        Assert.Equal("203.0.113.1", recorded.IP_ADDRESS);
    }

    [Fact]
    public async Task CreateAsync_WhenGuestAndFreePreview_IssuesSessionWithGuestWatermark()
    {
        var sessionRepo = new FakePlaybackSessionRepository();
        var assetRepo = new FakeMediaAssetRepository();
        var provider = new FakeVideoProvider();
        var learning = new FakeLearningAccessContract { AccessGranted = true }; // Free preview allows access
        var catalog = new FakeCatalogPriceContract();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var service = new PlaybackSessionService(sessionRepo, assetRepo, provider, learning, catalog, clock);

        var episodeId = Guid.NewGuid();
        var asset = MEDIA_ASSET.Create("BunnyStream", "vid-free", Guid.NewGuid(), true);
        asset.MarkProcessing();
        asset.MarkReady("playback-free", 180, "https://cdn/thumb.jpg", clock);
        assetRepo.Add(asset);

        var command = new CreatePlaybackSessionCommand(episodeId, asset.MEDIA_ASSET_ID, "guest-device");
        var result = await service.CreateAsync(null, Guid.Empty, "198.51.100.1", command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("Guest", result.Value.WatermarkPayload);
        Assert.Single(sessionRepo.Sessions);
        Assert.Equal("guest-device", sessionRepo.Sessions[0].DEVICE_ID);
    }

    [Fact]
    public async Task GetByEpisodeIdAsync_ResolvesMediaAssetAndReturnsSignedPlayback()
    {
        var sessionRepo = new FakePlaybackSessionRepository();
        var assetRepo = new FakeMediaAssetRepository();
        var provider = new FakeVideoProvider();
        var learning = new FakeLearningAccessContract();
        var catalog = new FakeCatalogPriceContract();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var episodeId = Guid.NewGuid();
        var asset = MEDIA_ASSET.Create("BunnyStream", "vid-ep", Guid.NewGuid(), true);
        asset.MarkProcessing();
        asset.MarkReady("playback-ep", 240, "https://cdn/thumb.jpg", clock);
        assetRepo.Add(asset);

        catalog.EpisodeMediaAssets[episodeId] = asset.MEDIA_ASSET_ID;

        var service = new PlaybackSessionService(sessionRepo, assetRepo, provider, learning, catalog, clock);

        var result = await service.GetByEpisodeIdAsync(
            Guid.NewGuid(), Guid.NewGuid(), "127.0.0.1", null, episodeId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("playlist.m3u8", result.Value.ManifestUrl);
    }

    [Fact]
    public async Task CreateAsync_WhenNoEnrollmentAccess_ReturnsForbidden()
    {
        var sessionRepo = new FakePlaybackSessionRepository();
        var assetRepo = new FakeMediaAssetRepository();
        var provider = new FakeVideoProvider();
        var learning = new FakeLearningAccessContract { AccessGranted = false };
        var catalog = new FakeCatalogPriceContract();
        var clock = new FakeClock(DateTime.UtcNow);

        var service = new PlaybackSessionService(sessionRepo, assetRepo, provider, learning, catalog, clock);

        var asset = MEDIA_ASSET.Create("BunnyStream", "vid-1", Guid.NewGuid(), true);
        asset.MarkProcessing();
        asset.MarkReady("playback-1", 300, "https://cdn/thumb.jpg", clock);
        assetRepo.Add(asset);

        var command = new CreatePlaybackSessionCommand(Guid.NewGuid(), asset.MEDIA_ASSET_ID, "device-1");
        var result = await service.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), null, command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("forbidden", result.Error.Code);
    }

    [Fact]
    public async Task CreateAsync_WhenAssetNotReady_ReturnsConflict()
    {
        var sessionRepo = new FakePlaybackSessionRepository();
        var assetRepo = new FakeMediaAssetRepository();
        var provider = new FakeVideoProvider();
        var learning = new FakeLearningAccessContract();
        var catalog = new FakeCatalogPriceContract();
        var clock = new FakeClock(DateTime.UtcNow);

        var service = new PlaybackSessionService(sessionRepo, assetRepo, provider, learning, catalog, clock);

        var asset = MEDIA_ASSET.Create("BunnyStream", "vid-1", Guid.NewGuid(), true);
        assetRepo.Add(asset); // Status is Uploading

        var command = new CreatePlaybackSessionCommand(Guid.NewGuid(), asset.MEDIA_ASSET_ID, null);
        var result = await service.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), null, command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("conflict", result.Error.Code);
    }
}
