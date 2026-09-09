using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;
using Siri.Integrations.Video.Bunny;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Siri.IntegrationTests.Fixtures;
using Siri.Integrations.Video;
using Siri.Modules.Media.Application;
using Siri.Modules.Media.Domain;
using Siri.Modules.Media.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

public sealed class MediaUploadLifecycleIntegrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    [Fact]
    public async Task SignedWebhook_ReadyThenDelayedFailure_PersistsReadyAcrossScopes()
    {
        var clock = new TestClock(DateTime.UtcNow);
        var (assetId, _, _, providerId) = await SeedUploadAsync(clock.UtcNow.AddHours(1));
        foreach (var callbackStatus in new[] { 3, 5, 3 })
        {
            using var scope = fixture.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var provider = new TestVideoProvider(providerId,
                callbackStatus == 5 ? VideoProcessingStatus.Failed : VideoProcessingStatus.Ready);
            var handler = new BunnyWebhookHandler(new MediaAssetRepository(db), provider,
                Options.Create(new VideoProviderOptions { LibraryId = "12345", ReadOnlyApiKey = "test-webhook-secret" }),
                clock, NullLogger<BunnyWebhookHandler>.Instance);
            var body = JsonSerializer.SerializeToUtf8Bytes(new BunnyWebhookPayload(12345, providerId, callbackStatus));
            var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("test-webhook-secret"), body));
            Assert.True((await handler.HandleSignedWebhookAsync(body, "v1", "hmac-sha256", signature, default)).IsSuccess);
        }
        using var verification = fixture.CreateScope();
        var asset = await verification.ServiceProvider.GetRequiredService<AppDbContext>()
            .MediaAssets().SingleAsync(a => a.MEDIA_ASSET_ID == assetId);
        Assert.Equal(MediaAssetStatus.Ready, asset.STATUS);
        Assert.Equal(13, asset.DURATION_SECONDS);
    }

    [Fact]
    public async Task CompleteAsync_TransferFinished_CompletedSessionIsStillPolledUntilReady()
    {
        var clock = new TestClock(DateTime.UtcNow.AddMinutes(5));
        var (assetId, sessionId, ownerId, providerId) = await SeedUploadAsync(clock.UtcNow.AddHours(1));
        var provider = new TestVideoProvider(providerId, VideoProcessingStatus.Processing);

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var service = new MediaUploadSessionService(
                new MediaUploadSessionRepository(db), new MediaAssetRepository(db), provider, clock);

            var completion = await service.CompleteAsync(ownerId, sessionId, CancellationToken.None);

            Assert.True(completion.IsSuccess);
            Assert.Equal(MediaUploadSessionStatus.Completed, completion.Value.Status);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(MediaAssetStatus.Processing, (await db.MediaAssets().SingleAsync(a => a.MEDIA_ASSET_ID == assetId)).STATUS);
            provider.Status = VideoProcessingStatus.Ready;
            await CreateJob(db, provider, clock).RunAsync(CancellationToken.None);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var asset = await db.MediaAssets().SingleAsync(a => a.MEDIA_ASSET_ID == assetId);
            Assert.Equal(MediaAssetStatus.Ready, asset.STATUS);
            Assert.Equal(providerId, asset.PLAYBACK_ID);
            Assert.Equal(13, asset.DURATION_SECONDS);
            Assert.Equal(clock.UtcNow, asset.READY_AT_UTC);
            Assert.Equal(MediaUploadSessionStatus.Completed,
                (await db.MediaUploadSessions().SingleAsync(s => s.MEDIA_UPLOAD_SESSION_ID == sessionId)).STATUS);
        }
    }

    [Theory]
    [InlineData(VideoProcessingStatus.Uploading, MediaAssetStatus.Uploading, MediaUploadSessionStatus.Pending)]
    [InlineData(VideoProcessingStatus.Processing, MediaAssetStatus.Processing, MediaUploadSessionStatus.Completed)]
    [InlineData(VideoProcessingStatus.Ready, MediaAssetStatus.Ready, MediaUploadSessionStatus.Completed)]
    [InlineData(VideoProcessingStatus.Failed, MediaAssetStatus.Failed, MediaUploadSessionStatus.Expired)]
    public async Task RunAsync_ClientDidNotComplete_RecoversUsingProviderStatus(
        VideoProcessingStatus providerStatus, MediaAssetStatus expectedAsset, MediaUploadSessionStatus expectedSession)
    {
        var clock = new TestClock(DateTime.UtcNow.AddMinutes(5));
        var (assetId, sessionId, _, providerId) = await SeedUploadAsync(clock.UtcNow.AddHours(1));
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await CreateJob(db, new TestVideoProvider(providerId, providerStatus), clock).RunAsync(CancellationToken.None);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(expectedAsset, (await db.MediaAssets().SingleAsync(a => a.MEDIA_ASSET_ID == assetId)).STATUS);
            Assert.Equal(expectedSession, (await db.MediaUploadSessions().SingleAsync(s => s.MEDIA_UPLOAD_SESSION_ID == sessionId)).STATUS);
        }
    }

    [Fact]
    public async Task RunAsync_ExpiredSessionWithoutCompletedTransfer_DoesNotMarkAssetReady()
    {
        var clock = new TestClock(DateTime.UtcNow.AddMinutes(5));
        var (assetId, sessionId, _, providerId) = await SeedUploadAsync(clock.UtcNow.AddMinutes(-1));
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await CreateJob(db, new TestVideoProvider(providerId, VideoProcessingStatus.Uploading), clock)
                .RunAsync(CancellationToken.None);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var asset = await db.MediaAssets().SingleAsync(a => a.MEDIA_ASSET_ID == assetId);
            Assert.Equal(MediaAssetStatus.Uploading, asset.STATUS);
            Assert.Null(asset.PLAYBACK_ID);
            Assert.Equal(MediaUploadSessionStatus.Expired,
                (await db.MediaUploadSessions().SingleAsync(s => s.MEDIA_UPLOAD_SESSION_ID == sessionId)).STATUS);
        }
    }

    [Fact]
    public async Task RunAsync_ProviderUnavailable_PreservesPendingUploadForRetry()
    {
        var clock = new TestClock(DateTime.UtcNow.AddMinutes(5));
        var (assetId, sessionId, _, providerId) = await SeedUploadAsync(clock.UtcNow.AddHours(1));
        var provider = new TestVideoProvider(providerId, VideoProcessingStatus.Ready) { Unavailable = true };
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            await CreateJob(db, provider, clock).RunAsync(CancellationToken.None);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(MediaAssetStatus.Uploading, (await db.MediaAssets().SingleAsync(a => a.MEDIA_ASSET_ID == assetId)).STATUS);
            Assert.Equal(MediaUploadSessionStatus.Pending,
                (await db.MediaUploadSessions().SingleAsync(s => s.MEDIA_UPLOAD_SESSION_ID == sessionId)).STATUS);
        }
    }

    private async Task<(Guid AssetId, Guid SessionId, Guid OwnerId, string ProviderId)> SeedUploadAsync(DateTime expiresAtUtc)
    {
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var ownerId = Guid.NewGuid();
        var providerId = Guid.NewGuid().ToString();
        var asset = MEDIA_ASSET.Create("BunnyStream", providerId, ownerId, true);
        var session = MEDIA_UPLOAD_SESSION.Create(asset.MEDIA_ASSET_ID, "https://video.bunnycdn.com/tusupload", expiresAtUtc);
        db.MediaAssets().Add(asset);
        db.MediaUploadSessions().Add(session);
        await db.SaveChangesAsync();
        return (asset.MEDIA_ASSET_ID, session.MEDIA_UPLOAD_SESSION_ID, ownerId, providerId);
    }

    private static BunnyTranscodePollJob CreateJob(AppDbContext db, IVideoProvider provider, IClock clock) =>
        new(db, provider, clock, NullLogger<BunnyTranscodePollJob>.Instance);

    private sealed class TestClock(DateTime utcNow) : IClock
    {
        // Audit timestamps are stored with millisecond precision by the database model.
        public DateTime UtcNow { get; } = new(
            utcNow.Ticks - utcNow.Ticks % TimeSpan.TicksPerMillisecond, DateTimeKind.Utc);
    }

    private sealed class TestVideoProvider(string providerId, VideoProcessingStatus status) : IVideoProvider
    {
        public VideoProcessingStatus Status { get; set; } = status;
        public bool Unavailable { get; init; }

        public Task<Result<VideoStatus>> GetStatusAsync(string providerVideoId, CancellationToken cancellationToken) =>
            Task.FromResult(providerVideoId != providerId || Unavailable
                ? Result.Failure<VideoStatus>(new DomainError("provider_unavailable", "Provider is unavailable."))
                : Result.Success(new VideoStatus(providerVideoId, Status, TimeSpan.FromSeconds(12.25))));

        public Task<Result<VideoAsset>> CreateVideoAsync(string title, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<VideoUploadUrl>> GetUploadUrlAsync(string providerVideoId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result> DeleteVideoAsync(string providerVideoId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result<SignedPlaybackUrl>> GetSignedPlaybackUrlAsync(string providerVideoId, TimeSpan timeToLive, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
