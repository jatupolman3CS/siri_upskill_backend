using Siri.Integrations.Video;
using Siri.Modules.Media.Application;
using Siri.Modules.Media.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Media;

public sealed class MediaUploadSessionServiceTests
{
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

    private sealed class FakeMediaUploadSessionRepository : IMediaUploadSessionRepository
    {
        public readonly Dictionary<Guid, MEDIA_UPLOAD_SESSION> Sessions = [];

        public Task<MEDIA_UPLOAD_SESSION?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Sessions.TryGetValue(id, out var session) ? session : null);

        public void Add(MEDIA_UPLOAD_SESSION uploadSession) => Sessions[uploadSession.MEDIA_UPLOAD_SESSION_ID] = uploadSession;

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
                new SignedPlaybackUrl("https://cdn/play.m3u8", DateTime.UtcNow.AddMinutes(5))));
    }

    [Fact]
    public async Task CreateAsync_WithOwnerUser_CreatesUploadSession()
    {
        var assetRepo = new FakeMediaAssetRepository();
        var sessionRepo = new FakeMediaUploadSessionRepository();
        var provider = new FakeVideoProvider();
        var service = new MediaUploadSessionService(sessionRepo, assetRepo, provider);

        var ownerId = Guid.NewGuid();
        var asset = MEDIA_ASSET.Create("BunnyStream", "vid-1", ownerId, true);
        assetRepo.Add(asset);

        var result = await service.CreateAsync(ownerId, asset.MEDIA_ASSET_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(asset.MEDIA_ASSET_ID, result.Value.MediaAssetId);
        Assert.Equal("https://upload.bunny.net/tus/vid-1", result.Value.UploadUrl);
        Assert.Equal(MediaUploadSessionStatus.Pending, result.Value.Status);
        Assert.Single(sessionRepo.Sessions);
    }

    [Fact]
    public async Task CompleteAsync_WithOwnerUser_CompletesSessionAndMarksAssetProcessing()
    {
        var assetRepo = new FakeMediaAssetRepository();
        var sessionRepo = new FakeMediaUploadSessionRepository();
        var provider = new FakeVideoProvider();
        var service = new MediaUploadSessionService(sessionRepo, assetRepo, provider);

        var ownerId = Guid.NewGuid();
        var asset = MEDIA_ASSET.Create("BunnyStream", "vid-1", ownerId, true);
        assetRepo.Add(asset);

        var session = MEDIA_UPLOAD_SESSION.Create(asset.MEDIA_ASSET_ID, "https://upload.bunny.net/tus/vid-1", DateTime.UtcNow.AddMinutes(30));
        sessionRepo.Add(session);

        var result = await service.CompleteAsync(ownerId, session.MEDIA_UPLOAD_SESSION_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MediaUploadSessionStatus.Completed, result.Value.Status);
        Assert.Equal(MediaAssetStatus.Processing, asset.STATUS);
    }
}
