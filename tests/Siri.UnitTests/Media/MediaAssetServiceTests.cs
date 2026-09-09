using Siri.Integrations.Video;
using Siri.Modules.Media.Application;
using Siri.Modules.Media.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Media;

public sealed class MediaAssetServiceTests
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

    private sealed class FakeVideoProvider : IVideoProvider
    {
        public bool CreateSuccess = true;
        public string CreatedVideoId = "bunny-vid-999";
        public bool DeleteCalled;
        public VideoProcessingStatus Status { get; set; } = VideoProcessingStatus.Ready;
        public int StatusCalls { get; private set; }

        public Task<Result<VideoAsset>> CreateVideoAsync(string title, CancellationToken cancellationToken)
        {
            return CreateSuccess
                ? Task.FromResult(Result.Success(new VideoAsset(CreatedVideoId, title)))
                : Task.FromResult(Result.Failure<VideoAsset>(DomainError.Validation("Provider failed")));
        }

        public Task<Result<VideoUploadUrl>> GetUploadUrlAsync(string providerVideoId, CancellationToken cancellationToken)
        {
            return Task.FromResult(Result.Success(
                new VideoUploadUrl("https://upload.bunny.net/tus", DateTime.UtcNow.AddHours(1))));
        }

        public Task<Result<VideoStatus>> GetStatusAsync(string providerVideoId, CancellationToken cancellationToken)
        {
            StatusCalls++;
            return Task.FromResult(Result.Success(
                new VideoStatus(providerVideoId, Status, TimeSpan.FromMinutes(2))));
        }

        public Task<Result> DeleteVideoAsync(string providerVideoId, CancellationToken cancellationToken)
        {
            DeleteCalled = true;
            return Task.FromResult(Result.Success());
        }

        public Task<Result<SignedPlaybackUrl>> GetSignedPlaybackUrlAsync(
            string providerVideoId,
            TimeSpan timeToLive,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(Result.Success(
                new SignedPlaybackUrl("https://cdn/play.m3u8", DateTime.UtcNow.AddMinutes(5))));
        }
    }

    [Fact]
    public async Task CreateAsync_WhenVideoProviderSucceeds_CreatesAssetAndSaves()
    {
        var repo = new FakeMediaAssetRepository();
        var provider = new FakeVideoProvider();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new MediaAssetService(repo, provider, clock);

        var uploaderId = Guid.NewGuid();
        var result = await service.CreateAsync(uploaderId, new CreateMediaAssetCommand("My Video Lesson"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("bunny-vid-999", result.Value.ProviderAssetId);
        Assert.Equal("BunnyStream", result.Value.Provider);
        Assert.Equal(uploaderId, result.Value.UploadedByUserId);
        Assert.Single(repo.Assets);
    }

    [Fact]
    public async Task GetByIdAsync_WithDifferentUser_ReturnsForbidden()
    {
        var repo = new FakeMediaAssetRepository();
        var provider = new FakeVideoProvider();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new MediaAssetService(repo, provider, clock);

        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var asset = MEDIA_ASSET.Create("BunnyStream", "vid-1", ownerId, true);
        repo.Add(asset);

        var result = await service.GetByIdAsync(otherUserId, asset.MEDIA_ASSET_ID, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("forbidden", result.Error.Code);
        Assert.Equal(0, provider.StatusCalls);
    }

    [Theory]
    [InlineData(VideoProcessingStatus.Uploading, MediaAssetStatus.Uploading)]
    [InlineData(VideoProcessingStatus.Processing, MediaAssetStatus.Processing)]
    [InlineData(VideoProcessingStatus.Ready, MediaAssetStatus.Ready)]
    [InlineData(VideoProcessingStatus.Failed, MediaAssetStatus.Failed)]
    public async Task GetByIdAsync_OwnerPolling_ReflectsProviderStatus(VideoProcessingStatus providerStatus, MediaAssetStatus expected)
    {
        var repo = new FakeMediaAssetRepository();
        var provider = new FakeVideoProvider { Status = providerStatus };
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new MediaAssetService(repo, provider, clock);
        var asset = MEDIA_ASSET.Create("BunnyStream", "vid-1", Guid.NewGuid(), true);
        repo.Add(asset);

        var result = await service.GetByIdAsync(asset.UPLOADED_BY_USER_ID, asset.MEDIA_ASSET_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(expected, result.Value.Status);
        Assert.Equal(expected, asset.STATUS);
        Assert.Equal(1, provider.StatusCalls);
        if (expected == MediaAssetStatus.Ready) Assert.Equal(120, result.Value.DurationSeconds);
    }

    [Fact]
    public async Task GetByIdAsync_AlreadyReady_DoesNotRegressOrQueryProvider()
    {
        var repo = new FakeMediaAssetRepository();
        var provider = new FakeVideoProvider { Status = VideoProcessingStatus.Uploading };
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new MediaAssetService(repo, provider, clock);
        var asset = MEDIA_ASSET.Create("BunnyStream", "vid-1", Guid.NewGuid(), true);
        asset.MarkReady("vid-1", 120, null, clock);
        repo.Add(asset);

        var result = await service.GetByIdAsync(asset.UPLOADED_BY_USER_ID, asset.MEDIA_ASSET_ID, CancellationToken.None);

        Assert.Equal(MediaAssetStatus.Ready, result.Value.Status);
        Assert.Equal(0, provider.StatusCalls);
    }

    [Fact]
    public async Task DeleteAsync_WhenAuthorized_DeletesFromProviderAndRepo()
    {
        var repo = new FakeMediaAssetRepository();
        var provider = new FakeVideoProvider();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new MediaAssetService(repo, provider, clock);

        var ownerId = Guid.NewGuid();
        var asset = MEDIA_ASSET.Create("BunnyStream", "vid-1", ownerId, true);
        repo.Add(asset);

        var result = await service.DeleteAsync(ownerId, asset.MEDIA_ASSET_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(provider.DeleteCalled);
        Assert.Empty(repo.Assets);
    }
}
