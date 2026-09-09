using Siri.Integrations.Video;
using Siri.Modules.Media.Application;
using Siri.Modules.Media.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Media;

public sealed class MediaUploadSessionServiceTests
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
        public VideoProcessingStatus Status { get; set; } = VideoProcessingStatus.Processing;
        public int StatusCalls { get; private set; }
        public bool StatusFails { get; set; }
        public Task<Result<VideoAsset>> CreateVideoAsync(string title, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new VideoAsset("vid-1", title)));

        public Task<Result<VideoUploadUrl>> GetUploadUrlAsync(string providerVideoId, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(
                new VideoUploadUrl("https://upload.bunny.net/tus/vid-1", DateTime.UtcNow.AddMinutes(30))));

        public Task<Result<VideoStatus>> GetStatusAsync(string providerVideoId, CancellationToken cancellationToken)
        {
            StatusCalls++;
            return Task.FromResult(StatusFails
                ? Result.Failure<VideoStatus>(new DomainError("provider_unavailable", "Provider unavailable"))
                : Result.Success(new VideoStatus(providerVideoId, Status, TimeSpan.FromMinutes(2))));
        }

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
        var service = new MediaUploadSessionService(sessionRepo, assetRepo, provider, new FakeClock(DateTime.UtcNow));

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
        var service = new MediaUploadSessionService(sessionRepo, assetRepo, provider, new FakeClock(DateTime.UtcNow));

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

    [Fact]
    public async Task CompleteAsync_BeforeProviderReceivesBytes_DoesNotCompleteOrGrantReady()
    {
        var (service, asset, session, provider) = CreatePendingUpload();
        provider.Status = VideoProcessingStatus.Uploading;

        var result = await service.CompleteAsync(asset.UPLOADED_BY_USER_ID, session.MEDIA_UPLOAD_SESSION_ID, CancellationToken.None);

        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(MediaUploadSessionStatus.Pending, session.STATUS);
        Assert.Equal(MediaAssetStatus.Uploading, asset.STATUS);
    }

    [Fact]
    public async Task CompleteAsync_ProviderReady_StoresVerifiedDurationAndIsIdempotent()
    {
        var (service, asset, session, provider) = CreatePendingUpload();
        provider.Status = VideoProcessingStatus.Ready;

        var result = await service.CompleteAsync(asset.UPLOADED_BY_USER_ID, session.MEDIA_UPLOAD_SESSION_ID, CancellationToken.None);
        var retry = await service.CompleteAsync(asset.UPLOADED_BY_USER_ID, session.MEDIA_UPLOAD_SESSION_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(retry.IsSuccess);
        Assert.Equal(MediaAssetStatus.Ready, asset.STATUS);
        Assert.Equal(120, asset.DURATION_SECONDS);
        Assert.Equal(1, provider.StatusCalls);
    }

    [Fact]
    public async Task CompleteAsync_ExpiredSession_RejectsWithoutCallingProvider()
    {
        var (service, asset, session, provider) = CreatePendingUpload(expired: true);

        var result = await service.CompleteAsync(asset.UPLOADED_BY_USER_ID, session.MEDIA_UPLOAD_SESSION_ID, CancellationToken.None);

        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(MediaUploadSessionStatus.Expired, session.STATUS);
        Assert.Equal(MediaAssetStatus.Uploading, asset.STATUS);
        Assert.Equal(0, provider.StatusCalls);
    }

    [Fact]
    public async Task CompleteAsync_OtherOwner_RejectsWithoutCallingProvider()
    {
        var (service, asset, session, provider) = CreatePendingUpload();

        var result = await service.CompleteAsync(Guid.NewGuid(), session.MEDIA_UPLOAD_SESSION_ID, CancellationToken.None);

        Assert.Equal("forbidden", result.Error.Code);
        Assert.Equal(MediaUploadSessionStatus.Pending, session.STATUS);
        Assert.Equal(MediaAssetStatus.Uploading, asset.STATUS);
        Assert.Equal(0, provider.StatusCalls);
    }

    [Fact]
    public async Task CompleteAsync_ProviderUnavailable_LeavesUploadRetryable()
    {
        var (service, asset, session, provider) = CreatePendingUpload();
        provider.StatusFails = true;

        var result = await service.CompleteAsync(asset.UPLOADED_BY_USER_ID, session.MEDIA_UPLOAD_SESSION_ID, CancellationToken.None);

        Assert.Equal("provider_unavailable", result.Error.Code);
        Assert.Equal(MediaUploadSessionStatus.Pending, session.STATUS);
        Assert.Equal(MediaAssetStatus.Uploading, asset.STATUS);
    }

    [Fact]
    public async Task CreateAsync_AlreadyProcessing_DoesNotCreateReplacementSession()
    {
        var (service, asset, _, _) = CreatePendingUpload();
        asset.MarkProcessing();

        var result = await service.CreateAsync(asset.UPLOADED_BY_USER_ID, asset.MEDIA_ASSET_ID, CancellationToken.None);

        Assert.Equal("conflict", result.Error.Code);
    }

    private static (MediaUploadSessionService Service, MEDIA_ASSET Asset, MEDIA_UPLOAD_SESSION Session, FakeVideoProvider Provider)
        CreatePendingUpload(bool expired = false)
    {
        var clock = new FakeClock(new DateTime(2026, 9, 8, 12, 0, 0, DateTimeKind.Utc));
        var assetRepo = new FakeMediaAssetRepository();
        var sessionRepo = new FakeMediaUploadSessionRepository();
        var provider = new FakeVideoProvider();
        var asset = MEDIA_ASSET.Create("BunnyStream", "vid-1", Guid.NewGuid(), true);
        assetRepo.Add(asset);
        var session = MEDIA_UPLOAD_SESSION.Create(asset.MEDIA_ASSET_ID, "https://upload.bunny.net/tus/vid-1",
            expired ? clock.UtcNow : clock.UtcNow.AddMinutes(30));
        sessionRepo.Add(session);
        return (new MediaUploadSessionService(sessionRepo, assetRepo, provider, clock), asset, session, provider);
    }
}
