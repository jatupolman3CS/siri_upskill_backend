using Microsoft.Extensions.Logging.Abstractions;
using Siri.Integrations.Video;
using Siri.Modules.Media.Application;
using Siri.Modules.Media.Domain;
using Siri.SharedKernel;

namespace Siri.UnitTests.Media;

/// <summary>P11-13: <c>MediaIngestContractService</c> — create the provider video, save the row, stream the bytes, mark Processing; clean up on any failure.</summary>
public sealed class MediaIngestContractServiceTests
{
    private static readonly Guid Owner = Guid.Parse("11111111-1111-7111-8111-111111111111");

    private sealed class Journal
    {
        public List<string> Events { get; } = [];
    }

    private sealed class FakeRepository(Journal journal) : IMediaAssetRepository
    {
        public Dictionary<Guid, MEDIA_ASSET> Rows { get; } = [];

        public List<MediaAssetStatus> StatusAtEachSave { get; } = [];

        /// <summary>1-based save number that throws, or 0 for never.</summary>
        public int FailSaveNumber { get; set; }

        private int _saves;
        private readonly List<MEDIA_ASSET> _pending = [];

        public Task<MEDIA_ASSET?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.GetValueOrDefault(id));

        public Task<MEDIA_ASSET?> GetByProviderAssetIdAsync(string providerAssetId, CancellationToken cancellationToken) =>
            Task.FromResult(Rows.Values.FirstOrDefault(a => a.PROVIDER_ASSET_ID == providerAssetId));

        public Task<(IReadOnlyList<MEDIA_ASSET> Items, int TotalCount)> GetPagedByUploaderAsync(
            Guid uploadedByUserId, int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult(((IReadOnlyList<MEDIA_ASSET>)[], 0));

        public void Add(MEDIA_ASSET mediaAsset)
        {
            journal.Events.Add("repo.add");
            _pending.Add(mediaAsset);
        }

        public void Remove(MEDIA_ASSET mediaAsset)
        {
            journal.Events.Add("repo.remove");
            _pending.Remove(mediaAsset);
            Rows.Remove(mediaAsset.MEDIA_ASSET_ID);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            _saves++;
            journal.Events.Add($"repo.save#{_saves}");
            if (FailSaveNumber == _saves)
            {
                throw new InvalidOperationException("database is down");
            }

            foreach (var asset in _pending)
            {
                Rows[asset.MEDIA_ASSET_ID] = asset;
                StatusAtEachSave.Add(asset.STATUS);
            }

            _pending.Clear();
            return Task.CompletedTask;
        }
    }

    private sealed class FakeProvider(Journal journal) : IVideoProvider
    {
        public string VideoId { get; set; } = "bunny-video-1";

        public Result<VideoAsset>? CreateResult { get; set; }

        public Result UploadResult { get; set; } = Result.Success();

        public Exception? UploadThrows { get; set; }

        public Result DeleteResult { get; set; } = Result.Success();

        public string? CreatedTitle { get; private set; }

        public byte[]? UploadedBytes { get; private set; }

        public long? UploadedLength { get; private set; }

        public string? UploadedTo { get; private set; }

        public List<string> Deleted { get; } = [];

        public bool DeleteTokenWasCancelled { get; private set; }

        public Task<Result<VideoAsset>> CreateVideoAsync(string title, CancellationToken cancellationToken)
        {
            journal.Events.Add("provider.create");
            CreatedTitle = title;
            return Task.FromResult(CreateResult ?? Result.Success(new VideoAsset(VideoId, title)));
        }

        public Task<Result<VideoUploadUrl>> GetUploadUrlAsync(string providerVideoId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public async Task<Result> UploadVideoAsync(string providerVideoId, Stream content, long? contentLength, CancellationToken cancellationToken)
        {
            journal.Events.Add("provider.upload");
            UploadedTo = providerVideoId;
            UploadedLength = contentLength;
            using var copy = new MemoryStream();
            await content.CopyToAsync(copy, cancellationToken);
            UploadedBytes = copy.ToArray();

            if (UploadThrows is not null)
            {
                throw UploadThrows;
            }

            return UploadResult;
        }

        public Task<Result<VideoStatus>> GetStatusAsync(string providerVideoId, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<Result> DeleteVideoAsync(string providerVideoId, CancellationToken cancellationToken)
        {
            journal.Events.Add("provider.delete");
            Deleted.Add(providerVideoId);
            DeleteTokenWasCancelled = cancellationToken.IsCancellationRequested;
            return Task.FromResult(DeleteResult);
        }

        public Task<Result<SignedPlaybackUrl>> GetSignedPlaybackUrlAsync(string providerVideoId, TimeSpan timeToLive, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    private static (MediaIngestContractService Service, FakeRepository Repo, FakeProvider Provider, Journal Journal) Create()
    {
        var journal = new Journal();
        var repo = new FakeRepository(journal);
        var provider = new FakeProvider(journal);
        return (new MediaIngestContractService(repo, provider, NullLogger<MediaIngestContractService>.Instance), repo, provider, journal);
    }

    // ---- success ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task IngestAsync_Success_CreatesAProcessingAssetOwnedByTheGivenUser()
    {
        var (service, repo, provider, _) = Create();
        var bytes = new byte[] { 1, 2, 3, 4, 5, 6 };

        var result = await service.IngestAsync(Owner, "บันทึก: คาบที่ 1", new MemoryStream(bytes), bytes.Length, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var asset = Assert.Single(repo.Rows.Values);
        Assert.Equal(result.Value, asset.MEDIA_ASSET_ID);
        Assert.Equal(Owner, asset.UPLOADED_BY_USER_ID);
        Assert.Equal("BunnyStream", asset.PROVIDER);
        Assert.Equal("bunny-video-1", asset.PROVIDER_ASSET_ID);
        Assert.True(asset.DRM_ENABLED);
        // Processing is what BunnyTranscodePollJob selects (Status == Processing), so the existing poll/webhook takes it to Ready.
        Assert.Equal(MediaAssetStatus.Processing, asset.STATUS);
        Assert.Equal("บันทึก: คาบที่ 1", provider.CreatedTitle);
        Assert.Equal("bunny-video-1", provider.UploadedTo);
        Assert.Equal(bytes, provider.UploadedBytes);
        Assert.Equal(bytes.Length, provider.UploadedLength);
        Assert.Empty(provider.Deleted);
    }

    [Fact]
    public async Task IngestAsync_SavesTheRowBeforeTheUploadStarts_SoACrashLeavesATraceableAsset()
    {
        var (service, repo, _, journal) = Create();

        await service.IngestAsync(Owner, "t", new MemoryStream(new byte[1]), 1, CancellationToken.None);

        Assert.Equal(["provider.create", "repo.add", "repo.save#1", "provider.upload", "repo.save#2"], journal.Events);
        Assert.Equal(MediaAssetStatus.Uploading, repo.StatusAtEachSave[0]);
    }

    [Fact]
    public async Task IngestAsync_TitleIsTrimmedAndCutToTheAssetTitleLimit()
    {
        var (service, _, provider, _) = Create();

        await service.IngestAsync(Owner, "  " + new string('ก', 300) + "  ", new MemoryStream(new byte[1]), 1, CancellationToken.None);

        Assert.Equal(CreateMediaAssetValidator.MaxTitleLength, provider.CreatedTitle!.Length);
    }

    [Fact]
    public async Task IngestAsync_TitleIsNeverCutInTheMiddleOfASurrogatePair()
    {
        var (service, _, provider, _) = Create();
        // 199 plain characters, then an emoji (two UTF-16 units) straddling the limit.
        var title = new string('a', CreateMediaAssetValidator.MaxTitleLength - 1) + "😀";

        await service.IngestAsync(Owner, title, new MemoryStream(new byte[1]), 1, CancellationToken.None);

        Assert.Equal(CreateMediaAssetValidator.MaxTitleLength - 1, provider.CreatedTitle!.Length);
        Assert.False(char.IsHighSurrogate(provider.CreatedTitle[^1]));
    }

    [Fact]
    public async Task IngestAsync_UnknownLength_IsPassedOnAsUnknown()
    {
        var (service, _, provider, _) = Create();

        await service.IngestAsync(Owner, "t", new MemoryStream(new byte[3]), null, CancellationToken.None);

        Assert.Null(provider.UploadedLength);
    }

    // ---- validation ------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("", "title", 1)]
    [InlineData("11111111-1111-7111-8111-111111111111", "   ", 1)]
    [InlineData("11111111-1111-7111-8111-111111111111", "title", -1)]
    public async Task IngestAsync_InvalidInput_FailsBeforeTouchingTheProvider(string ownerText, string title, long length)
    {
        var (service, repo, provider, journal) = Create();
        var owner = ownerText.Length == 0 ? Guid.Empty : Guid.Parse(ownerText);

        var result = await service.IngestAsync(owner, title, new MemoryStream(new byte[1]), length, CancellationToken.None);

        Assert.Equal("validation", result.Error.Code);
        Assert.Empty(journal.Events);
        Assert.Empty(repo.Rows);
        Assert.Empty(provider.Deleted);
    }

    [Fact]
    public async Task IngestAsync_NullContent_Throws()
    {
        var (service, _, _, _) = Create();

        await Assert.ThrowsAsync<ArgumentNullException>(() => service.IngestAsync(Owner, "t", null!, 1, CancellationToken.None));
    }

    // ---- failures + cleanup -----------------------------------------------------------------------------------------

    [Fact]
    public async Task IngestAsync_ProviderNotConfigured_FailsWithItsCodeAndLeavesNothing()
    {
        var (service, repo, provider, journal) = Create();
        provider.CreateResult = Result.Failure<VideoAsset>(VideoProviderErrors.ProviderNotConfigured());

        var result = await service.IngestAsync(Owner, "t", new MemoryStream(new byte[1]), 1, CancellationToken.None);

        Assert.Equal(VideoProviderErrors.ProviderNotConfiguredCode, result.Error.Code);
        Assert.Equal(["provider.create"], journal.Events);
        Assert.Empty(repo.Rows);
    }

    [Fact]
    public async Task IngestAsync_UploadRefused_DeletesTheProviderVideoAndRemovesTheRow()
    {
        var (service, repo, provider, journal) = Create();
        provider.UploadResult = Result.Failure(new DomainError("video.provider_error", "Bunny Stream upload failed (network)."));

        var result = await service.IngestAsync(Owner, "t", new MemoryStream(new byte[1]), 1, CancellationToken.None);

        Assert.Equal("video.provider_error", result.Error.Code);
        Assert.Equal(["bunny-video-1"], provider.Deleted);
        Assert.Empty(repo.Rows);
        Assert.Equal(
            ["provider.create", "repo.add", "repo.save#1", "provider.upload", "provider.delete", "repo.remove", "repo.save#2"],
            journal.Events);
    }

    [Fact]
    public async Task IngestAsync_UploadThrows_CleansUpThenLetsTheExceptionThrough()
    {
        var (service, repo, provider, _) = Create();
        provider.UploadThrows = new IOException("stream broke");

        await Assert.ThrowsAsync<IOException>(() => service.IngestAsync(Owner, "t", new MemoryStream(new byte[1]), 1, CancellationToken.None));

        Assert.Equal(["bunny-video-1"], provider.Deleted);
        Assert.Empty(repo.Rows);
    }

    [Fact]
    public async Task IngestAsync_CallerCancelsMidUpload_StillCleansUpWithALiveToken()
    {
        var (service, repo, provider, _) = Create();
        using var cts = new CancellationTokenSource();
        provider.UploadThrows = new OperationCanceledException(cts.Token);
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.IngestAsync(Owner, "t", new MemoryStream(new byte[1]), 1, CancellationToken.None));
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => service.IngestAsync(Owner, "t", new MemoryStream(new byte[1]), 1, cts.Token));

        Assert.False(provider.DeleteTokenWasCancelled);
        Assert.Empty(repo.Rows);
    }

    [Fact]
    public async Task IngestAsync_ProviderRefusesTheCleanupDelete_KeepsTheRowAsFailedSoTheVideoIsNotLost()
    {
        var (service, repo, provider, _) = Create();
        provider.UploadResult = Result.Failure(new DomainError("video.provider_error", "x"));
        provider.DeleteResult = Result.Failure(new DomainError("video.provider_error", "bunny is down"));

        var result = await service.IngestAsync(Owner, "t", new MemoryStream(new byte[1]), 1, CancellationToken.None);

        Assert.True(result.IsFailure);
        var asset = Assert.Single(repo.Rows.Values);
        Assert.Equal(MediaAssetStatus.Failed, asset.STATUS);
        Assert.Equal("bunny-video-1", asset.PROVIDER_ASSET_ID);
    }

    [Fact]
    public async Task IngestAsync_TheFinalSaveFails_DeletesTheUploadedVideoAndRemovesTheRow()
    {
        var (service, repo, provider, _) = Create();
        repo.FailSaveNumber = 2;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IngestAsync(Owner, "t", new MemoryStream(new byte[1]), 1, CancellationToken.None));

        Assert.Equal(["bunny-video-1"], provider.Deleted);
        Assert.Empty(repo.Rows);
    }

    [Fact]
    public async Task IngestAsync_TheFirstSaveFails_DeletesTheProviderVideoAndNeverUploads()
    {
        var (service, repo, provider, journal) = Create();
        repo.FailSaveNumber = 1;

        await Assert.ThrowsAsync<InvalidOperationException>(() => service.IngestAsync(Owner, "t", new MemoryStream(new byte[1]), 1, CancellationToken.None));

        Assert.DoesNotContain("provider.upload", journal.Events);
        Assert.Equal(["bunny-video-1"], provider.Deleted);
        Assert.Empty(repo.Rows);
        // The never-saved row was detached, so a later SaveChanges in the same scope cannot insert it by accident.
        Assert.Contains("repo.remove", journal.Events);
    }

    [Fact]
    public async Task IngestAsync_ACleanupThatItselfFails_DoesNotMaskTheOriginalFailure()
    {
        var (service, repo, provider, _) = Create();
        provider.UploadThrows = new IOException("stream broke");
        repo.FailSaveNumber = 2; // the cleanup's own save fails too

        // The IOException from the upload, not the cleanup's InvalidOperationException, is what the caller sees.
        await Assert.ThrowsAsync<IOException>(() => service.IngestAsync(Owner, "t", new MemoryStream(new byte[1]), 1, CancellationToken.None));
    }
}
