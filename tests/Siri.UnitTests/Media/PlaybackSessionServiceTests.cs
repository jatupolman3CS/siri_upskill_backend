using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Siri.Integrations.Video;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Identity.Contracts;
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

    private sealed class FakeUserContactReader : IUserContactReader
    {
        public readonly Dictionary<Guid, (string? Email, string? DisplayName)> Contacts = [];
        public readonly List<Guid> RequestedUserIds = [];
        public Exception? ExceptionToThrow { get; set; }

        public Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Contacts.TryGetValue(userId, out var contact) ? contact.Email : null);

        public Task<(string? Email, string? DisplayName)> GetUserContactInfoAsync(Guid userId, CancellationToken cancellationToken)
        {
            RequestedUserIds.Add(userId);

            if (ExceptionToThrow is not null)
            {
                throw ExceptionToThrow;
            }

            return Task.FromResult(Contacts.TryGetValue(userId, out var contact) ? contact : (null, null));
        }
    }

    private sealed class ListLogger : ILogger<PlaybackSessionService>
    {
        public readonly List<(LogLevel Level, string Message)> Entries = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            // Include the exception text too: a PII leak through the exception object must fail the tests.
            Entries.Add((logLevel, formatter(state, exception) + (exception?.ToString() ?? string.Empty)));
        }
    }

    [Fact]
    public async Task CreateAsync_WhenAssetIsReady_IssuesSignedUrlAndWatermark()
    {
        var sessionRepo = new FakePlaybackSessionRepository();
        var assetRepo = new FakeMediaAssetRepository();
        var provider = new FakeVideoProvider();
        var learning = new FakeLearningAccessContract();
        var catalog = new FakeCatalogPriceContract();
        var contacts = new FakeUserContactReader();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var service = new PlaybackSessionService(sessionRepo, assetRepo, provider, learning, catalog, contacts, NullLogger<PlaybackSessionService>.Instance, clock);

        var userId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        contacts.Contacts[userId] = ("jane.doe@example.com", "Jane Doe");

        var asset = MEDIA_ASSET.Create("BunnyStream", "vid-1", Guid.NewGuid(), true);
        asset.MarkProcessing();
        asset.MarkReady("playback-1", 300, "https://cdn/thumb.jpg", clock);
        assetRepo.Add(asset);

        var command = new CreatePlaybackSessionCommand(episodeId, asset.MEDIA_ASSET_ID, "device-chrome");
        var result = await service.CreateAsync(userId, sessionId, "203.0.113.1", command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains("playlist.m3u8", result.Value.ManifestUrl);
        Assert.Equal("Jane Doe · jane.doe@example.com · 2026-08-21 10:00:00 UTC", result.Value.WatermarkPayload);
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
        var contacts = new FakeUserContactReader();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var service = new PlaybackSessionService(sessionRepo, assetRepo, provider, learning, catalog, contacts, NullLogger<PlaybackSessionService>.Instance, clock);

        var episodeId = Guid.NewGuid();
        var asset = MEDIA_ASSET.Create("BunnyStream", "vid-free", Guid.NewGuid(), true);
        asset.MarkProcessing();
        asset.MarkReady("playback-free", 180, "https://cdn/thumb.jpg", clock);
        assetRepo.Add(asset);

        var command = new CreatePlaybackSessionCommand(episodeId, asset.MEDIA_ASSET_ID, "guest-device");
        var result = await service.CreateAsync(null, Guid.Empty, "198.51.100.1", command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("SIRI UpSkill · Guest · 2026-08-21 10:00:00 UTC", result.Value.WatermarkPayload);
        Assert.Empty(contacts.RequestedUserIds); // a guest has no account to look up — nothing may be invented
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
        var contacts = new FakeUserContactReader();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var episodeId = Guid.NewGuid();
        var asset = MEDIA_ASSET.Create("BunnyStream", "vid-ep", Guid.NewGuid(), true);
        asset.MarkProcessing();
        asset.MarkReady("playback-ep", 240, "https://cdn/thumb.jpg", clock);
        assetRepo.Add(asset);

        catalog.EpisodeMediaAssets[episodeId] = asset.MEDIA_ASSET_ID;

        var service = new PlaybackSessionService(sessionRepo, assetRepo, provider, learning, catalog, contacts, NullLogger<PlaybackSessionService>.Instance, clock);

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
        var contacts = new FakeUserContactReader();
        var clock = new FakeClock(DateTime.UtcNow);

        var service = new PlaybackSessionService(sessionRepo, assetRepo, provider, learning, catalog, contacts, NullLogger<PlaybackSessionService>.Instance, clock);

        var asset = MEDIA_ASSET.Create("BunnyStream", "vid-1", Guid.NewGuid(), true);
        asset.MarkProcessing();
        asset.MarkReady("playback-1", 300, "https://cdn/thumb.jpg", clock);
        assetRepo.Add(asset);

        var command = new CreatePlaybackSessionCommand(Guid.NewGuid(), asset.MEDIA_ASSET_ID, "device-1");
        var result = await service.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), null, command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("forbidden", result.Error.Code);
        Assert.Empty(contacts.RequestedUserIds); // a denied request never reads the viewer's name/email
    }

    [Fact]
    public async Task CreateAsync_WhenAssetNotReady_ReturnsConflict()
    {
        var sessionRepo = new FakePlaybackSessionRepository();
        var assetRepo = new FakeMediaAssetRepository();
        var provider = new FakeVideoProvider();
        var learning = new FakeLearningAccessContract();
        var catalog = new FakeCatalogPriceContract();
        var contacts = new FakeUserContactReader();
        var clock = new FakeClock(DateTime.UtcNow);

        var service = new PlaybackSessionService(sessionRepo, assetRepo, provider, learning, catalog, contacts, NullLogger<PlaybackSessionService>.Instance, clock);

        var asset = MEDIA_ASSET.Create("BunnyStream", "vid-1", Guid.NewGuid(), true);
        assetRepo.Add(asset); // Status is Uploading

        var command = new CreatePlaybackSessionCommand(Guid.NewGuid(), asset.MEDIA_ASSET_ID, null);
        var result = await service.CreateAsync(Guid.NewGuid(), Guid.NewGuid(), null, command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("conflict", result.Error.Code);
    }

    /// <summary>A service wired with a ready asset, a capturing logger and a fixed clock, for the watermark (SE-02 / Q8) tests.</summary>
    private sealed class WatermarkHarness
    {
        public WatermarkHarness()
        {
            var clock = new FakeClock(new DateTime(2026, 10, 9, 8, 7, 6, DateTimeKind.Utc));

            var asset = MEDIA_ASSET.Create("BunnyStream", "vid-wm", Guid.NewGuid(), true);
            asset.MarkProcessing();
            asset.MarkReady("playback-wm", 120, "https://cdn/thumb.jpg", clock);
            AssetId = asset.MEDIA_ASSET_ID;

            var assetRepo = new FakeMediaAssetRepository();
            assetRepo.Add(asset);

            Service = new PlaybackSessionService(
                Sessions,
                assetRepo,
                new FakeVideoProvider(),
                new FakeLearningAccessContract(),
                new FakeCatalogPriceContract(),
                Contacts,
                Logger,
                clock);
        }

        public Guid UserId { get; } = Guid.NewGuid();

        public Guid AssetId { get; }

        public FakePlaybackSessionRepository Sessions { get; } = new();

        public FakeUserContactReader Contacts { get; } = new();

        public ListLogger Logger { get; } = new();

        public PlaybackSessionService Service { get; }

        public Task<Result<PlaybackSessionResponse>> PlayAsync(Guid? userId) =>
            Service.CreateAsync(
                userId,
                Guid.NewGuid(),
                "203.0.113.9",
                new CreatePlaybackSessionCommand(Guid.NewGuid(), AssetId, "dev-wm"),
                CancellationToken.None);
    }

    [Fact]
    public async Task CreateAsync_NamedUser_WatermarkHasNameEmailAndTimestampFromServerLookup()
    {
        var h = new WatermarkHarness();
        h.Contacts.Contacts[h.UserId] = ("somchai@example.com", "Somchai Jaidee");

        var result = await h.PlayAsync(h.UserId);

        Assert.True(result.IsSuccess);
        Assert.Equal("Somchai Jaidee · somchai@example.com · 2026-10-09 08:07:06 UTC", result.Value.WatermarkPayload);
        Assert.Equal([h.UserId], h.Contacts.RequestedUserIds); // looked up by the authenticated user id only
        Assert.Empty(h.Logger.Entries); // happy path logs nothing — in particular not the payload or the email
    }

    [Fact]
    public async Task CreateAsync_UserWithoutDisplayName_WatermarkFallsBackToEmailAndTimestamp()
    {
        var h = new WatermarkHarness();
        h.Contacts.Contacts[h.UserId] = ("no.name@example.com", "   ");

        var result = await h.PlayAsync(h.UserId);

        Assert.True(result.IsSuccess);
        Assert.Equal("no.name@example.com · 2026-10-09 08:07:06 UTC", result.Value.WatermarkPayload);
        var warning = Assert.Single(h.Logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.DoesNotContain("no.name@example.com", warning.Message);
    }

    [Fact]
    public async Task CreateAsync_UserWithoutEmail_WatermarkKeepsNameAndAddsUserIdSoItStaysUnique()
    {
        var h = new WatermarkHarness();
        h.Contacts.Contacts[h.UserId] = (null, "Somchai Jaidee");

        var result = await h.PlayAsync(h.UserId);

        Assert.True(result.IsSuccess);
        Assert.Equal($"Somchai Jaidee · {h.UserId} · 2026-10-09 08:07:06 UTC", result.Value.WatermarkPayload);
        var warning = Assert.Single(h.Logger.Entries);
        Assert.Equal(LogLevel.Warning, warning.Level);
        Assert.DoesNotContain("Somchai", warning.Message);
    }

    [Fact]
    public async Task CreateAsync_UserNotFoundByContactReader_WatermarkFallsBackToUserIdForm()
    {
        var h = new WatermarkHarness(); // no contact registered -> reader returns (null, null)

        var result = await h.PlayAsync(h.UserId);

        Assert.True(result.IsSuccess);
        Assert.Equal($"SIRI UpSkill · {h.UserId} · 2026-10-09 08:07:06 UTC", result.Value.WatermarkPayload);
        Assert.Single(h.Sessions.Sessions); // playback is not blocked
        Assert.Equal(LogLevel.Warning, Assert.Single(h.Logger.Entries).Level);
    }

    [Fact]
    public async Task CreateAsync_ContactLookupThrows_PlaybackStillSucceedsWithUserIdFormAndLogsNoPii()
    {
        var h = new WatermarkHarness();
        h.Contacts.Contacts[h.UserId] = ("leaky@example.com", "Leaky Name");
        // The exception text itself carries PII on purpose: neither its message nor the object may reach the log.
        h.Contacts.ExceptionToThrow = new InvalidOperationException("db down for leaky@example.com / Leaky Name");

        var result = await h.PlayAsync(h.UserId);

        Assert.True(result.IsSuccess);
        Assert.Equal($"SIRI UpSkill · {h.UserId} · 2026-10-09 08:07:06 UTC", result.Value.WatermarkPayload);
        Assert.Single(h.Sessions.Sessions);
        Assert.NotEmpty(h.Logger.Entries);
        Assert.All(h.Logger.Entries, entry =>
        {
            Assert.Equal(LogLevel.Warning, entry.Level);
            Assert.DoesNotContain("leaky@example.com", entry.Message);
            Assert.DoesNotContain("Leaky Name", entry.Message);
        });
    }

    [Fact]
    public async Task CreateAsync_ContactLookupCancelled_PropagatesCancellationInsteadOfSwallowingIt()
    {
        var h = new WatermarkHarness();
        h.Contacts.ExceptionToThrow = new OperationCanceledException();

        await Assert.ThrowsAsync<OperationCanceledException>(() => h.PlayAsync(h.UserId));

        Assert.Empty(h.Sessions.Sessions);
    }

    [Fact]
    public async Task CreateAsync_GuestUser_WatermarkIsNeutralLabelWithoutAnyLookup()
    {
        var h = new WatermarkHarness();

        var result = await h.PlayAsync(null);

        Assert.True(result.IsSuccess);
        Assert.Equal("SIRI UpSkill · Guest · 2026-10-09 08:07:06 UTC", result.Value.WatermarkPayload);
        Assert.Empty(h.Contacts.RequestedUserIds);
        Assert.Empty(h.Logger.Entries);
    }

    [Fact]
    public async Task CreateAsync_NameAndEmailWithControlCharsAndNewlines_PayloadIsSingleLineAndSanitised()
    {
        var h = new WatermarkHarness();
        h.Contacts.Contacts[h.UserId] = ("evil@example.com\r\nX-Injected: 1", "Jane\r\n\tDoe\u0000 · fake@victim.com");

        var result = await h.PlayAsync(h.UserId);

        Assert.True(result.IsSuccess);
        var payload = result.Value.WatermarkPayload;
        Assert.DoesNotContain(payload, c => char.IsControl(c));
        Assert.Equal("Jane Doe fake@victim.com · evil@example.com X-Injected: 1 · 2026-10-09 08:07:06 UTC", payload);
    }

    [Fact]
    public async Task CreateAsync_VeryLongNameAndEmail_PayloadIsCappedAndKeepsTimestamp()
    {
        var h = new WatermarkHarness();
        h.Contacts.Contacts[h.UserId] = (new string('e', 200) + "@example.com", new string('N', 300));

        var result = await h.PlayAsync(h.UserId);

        Assert.True(result.IsSuccess);
        var payload = result.Value.WatermarkPayload;
        Assert.True(payload.Length <= WatermarkPayloadBuilder.MaxLength, $"payload was {payload.Length} chars");
        Assert.EndsWith(" · 2026-10-09 08:07:06 UTC", payload);
    }
}
