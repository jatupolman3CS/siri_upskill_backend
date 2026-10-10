using System.Net.Http;
using System.Reflection;
using Hangfire;
using Siri.Integrations.Google;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.Modules.Live.Infrastructure;
using Siri.SharedKernel;
using Siri.SharedKernel.Contracts;

namespace Siri.UnitTests.Live;

/// <summary>
/// The P11-13 import job (docs/contracts/P11-13-live-recording-auto-import.md section 6), every transition, with fake Google / video provider / Catalog and a fake
/// clock: discovery, the Waiting search and its backoff, the Transferring claim and lease, Processing and the attach, every failure mapping, the instructor alerts,
/// error isolation between rows, and "no secret in any log".
/// </summary>
public class LiveRecordingImportJobTests
{
    private static readonly DateTime Now = LiveTestData.Now;
    private static readonly Guid InstructorId = RecordingJobHarness.InstructorId;

    private readonly RecordingJobHarness _h = new();

    private async Task RunAsync() => await _h.RunAsync();

    /// <summary>A workspace instructor with recording access, an ended class with a Google room and a due Waiting import — the starting point of most tests.</summary>
    private (LiveSessionContext Context, SESSION_RECORDING_IMPORT Import) DueImport(TimeSpan? endedAgo = null, Action<SESSION_RECORDING_IMPORT>? shape = null)
    {
        if (_h.Accounts.Accounts.Count == 0)
        {
            _h.Account();
        }

        var context = _h.Session(endedAgo);
        var import = _h.Import(context);
        shape?.Invoke(import);
        return (context, import);
    }

    private SESSION_RECORDING_IMPORT ProcessingImport(LiveSessionContext context, Guid assetId, DateTime? nextAttemptAtUtc = null, DateTime? deadlineUtc = null)
    {
        var import = _h.Import(context);
        import.BeginTransfer("rec-name", "file-id", Now.AddHours(3));
        import.MarkProcessing(assetId, nextAttemptAtUtc ?? Now.AddMinutes(-1), deadlineUtc ?? Now.AddHours(5));
        return import;
    }

    private Guid ReadyAsset(Guid? assetId = null, string status = "Ready", int? duration = 3600)
    {
        var id = assetId ?? Guid.NewGuid();
        _h.Assets.Assets[id] = new MediaAssetSummary(id, InstructorId, status, duration);
        return id;
    }

    // ---- The switch ------------------------------------------------------------------------------------

    [Fact]
    public async Task Run_FeatureOff_DoesNothingAtAll()
    {
        _h.Enabled = false;
        var (_, import) = DueImport();
        _h.GoogleHasRecording(RecordingJobHarness.Finished(Now.AddHours(-2), TimeSpan.FromHours(1)));

        await RunAsync();

        Assert.Empty(_h.Attacher.ListByInstructorCalls);
        Assert.Empty(_h.Recordings.FindCalls);
        Assert.Equal(0, _h.Imports.SaveCount);
        Assert.Equal(RecordingImportStatus.Waiting, import.STATUS);
        Assert.Empty(_h.Alerts.RecordingImportedAlerts);
    }

    [Fact]
    public async Task Run_ManualOnlyRooms_DoesNothingAtAll()
    {
        _h.Mode = LiveProviderMode.ManualOnly;
        DueImport();

        await RunAsync();

        Assert.Empty(_h.Attacher.ListByInstructorCalls);
        Assert.Empty(_h.Recordings.FindCalls);
    }

    [Fact]
    public void RunAsync_IsSerializedByAStorageLock_WithoutAutomaticRetries()
    {
        var method = typeof(LiveRecordingImportJob).GetMethod(nameof(LiveRecordingImportJob.RunAsync))!;

        Assert.NotNull(method.GetCustomAttribute<DisableConcurrentExecutionAttribute>());
        Assert.Equal(0, method.GetCustomAttribute<AutomaticRetryAttribute>()!.Attempts);
    }

    // ---- Discovery -------------------------------------------------------------------------------------

    [Fact]
    public async Task Discovery_WorkspaceInstructorWithRecordingAccess_GetsAWaitingRow_DueAtTheEndPlusTheFirstSearchDelay()
    {
        _h.Account();
        var session = _h.Session();

        await _h.Job().DiscoverAsync(CancellationToken.None);

        var row = Assert.Single(_h.Imports.Imports);
        Assert.Equal(session.SessionId, row.SESSION_ID);
        Assert.Equal(session.CourseId, row.COURSE_ID);
        Assert.Equal(InstructorId, row.INSTRUCTOR_USER_ID);
        Assert.Equal(RecordingImportStatus.Waiting, row.STATUS);
        Assert.Equal(session.EndsAtUtc.AddMinutes(10), row.NEXT_ATTEMPT_AT_UTC);
        Assert.Equal(session.EndsAtUtc.AddHours(12), row.SEARCH_UNTIL_UTC);
        Assert.Equal(1, _h.Imports.SaveCount);
    }

    [Fact]
    public async Task Discovery_UsesTheConfiguredDelayAndWindow_AndLooksBackFortyEightHours()
    {
        _h.ConfigureAutoImport = a =>
        {
            a.FirstSearchDelayMinutes = 30;
            a.SearchWindowHours = 4;
        };
        _h.Account();
        var session = _h.Session(endedAgo: TimeSpan.FromHours(2));

        await _h.Job().DiscoverAsync(CancellationToken.None);

        var call = Assert.Single(_h.Attacher.ListByInstructorCalls);
        Assert.Equal([InstructorId], call.InstructorUserIds); // only instructors who could be imported at all are asked about
        Assert.Equal(Now - TimeSpan.FromHours(48), call.After);
        Assert.Equal(Now - TimeSpan.FromMinutes(30), call.Before);
        Assert.Equal(LiveRecordingImportJob.DiscoveryPageSize, call.Limit);

        var row = Assert.Single(_h.Imports.Imports);
        Assert.Equal(session.EndsAtUtc.AddMinutes(30), row.NEXT_ATTEMPT_AT_UTC);
        Assert.Equal(session.EndsAtUtc.AddHours(4), row.SEARCH_UNTIL_UTC);
    }

    [Fact]
    public async Task Discovery_AClassThatEndedTooRecentlyOrTooLongAgo_IsNotConsidered()
    {
        _h.Account();
        _h.Session(endedAgo: TimeSpan.FromMinutes(5));
        _h.Session(endedAgo: TimeSpan.FromHours(49));

        await _h.Job().DiscoverAsync(CancellationToken.None);

        Assert.Empty(_h.Imports.Imports);
    }

    [Fact]
    public async Task Discovery_PersonalAccount_GetsNoRow_TheManualPathCoversIt()
    {
        _h.Account(hostedDomain: null);
        _h.Session();

        await _h.Job().DiscoverAsync(CancellationToken.None);

        Assert.Empty(_h.Imports.Imports);
    }

    [Fact]
    public async Task Discovery_WorkspaceWithoutTheRecordingScopes_GetsNoRow()
    {
        _h.Account(scopes: GoogleScopes.CalendarEventsOwned);
        _h.Session();

        await _h.Job().DiscoverAsync(CancellationToken.None);

        Assert.Empty(_h.Imports.Imports);
    }

    [Fact]
    public async Task Discovery_NoAccountOrARevokedOne_GetsNoRow()
    {
        _h.Session();
        await _h.Job().DiscoverAsync(CancellationToken.None);
        Assert.Empty(_h.Imports.Imports);

        _h.Account().MarkRevoked(GoogleAccountRevokedReason.InvalidGrant, _h.Clock);
        await _h.Job().DiscoverAsync(CancellationToken.None);
        Assert.Empty(_h.Imports.Imports);
    }

    [Fact]
    public async Task Discovery_CancelledClass_ClassWithARecordingLesson_AndClassWithoutAGoogleRoom_GetNoRow()
    {
        _h.Account();
        _h.Session(status: LiveSessionStatus.Cancelled);
        _h.Session(recordingEpisodeId: Guid.NewGuid());
        _h.Session(withRoom: false);
        _h.Session(roomProvider: MeetingProvider.Manual, roomUrl: "https://zoom.us/j/123");

        await _h.Job().DiscoverAsync(CancellationToken.None);

        Assert.Empty(_h.Imports.Imports);
        Assert.Equal(0, _h.Imports.SaveCount);
    }

    [Fact]
    public async Task Discovery_ASessionThatAlreadyHasARow_IsLeftAlone()
    {
        _h.Account();
        var session = _h.Session();
        var existing = _h.Import(session);
        existing.MarkNoRecording(RecordingImportErrorCodes.NoRecordingFound, _h.Clock);

        await _h.Job().DiscoverAsync(CancellationToken.None);

        Assert.Same(existing, Assert.Single(_h.Imports.Imports));
        Assert.Equal(RecordingImportStatus.NoRecording, existing.STATUS);
    }

    [Fact]
    public async Task Discovery_AnAccountOfUnknownKind_IsNotACandidate_NoGoogleCallIsMade_AndCatalogIsNotAsked()
    {
        // An account of unknown kind can never hold the recording scopes (they are only granted after the kind was stamped), so it is never worth a Catalog query,
        // let alone a Google userinfo call.
        _h.Account(checkedKind: false);
        _h.Session();

        await _h.Job().DiscoverAsync(CancellationToken.None);

        Assert.Empty(_h.Imports.Imports);
        Assert.Empty(_h.Attacher.ListByInstructorCalls);
        Assert.Equal(0, _h.OAuth.RefreshCalls);
        Assert.Equal(0, _h.OAuth.UserInfoCalls);
        Assert.Equal(GoogleAccountKind.Unknown, _h.Accounts.Accounts[0].AccountKind);
    }

    [Fact]
    public async Task Discovery_WithNoInstructorOnTheAutomaticPath_NeverAsksCatalog()
    {
        _h.Account(scopes: Siri.Integrations.Google.GoogleScopes.CalendarEventsOwned);
        _h.Session();

        await _h.Job().DiscoverAsync(CancellationToken.None);

        Assert.Empty(_h.Attacher.ListByInstructorCalls);
        Assert.Empty(_h.Imports.Imports);
    }

    [Fact]
    public async Task Discovery_LosingTheRaceOnTheUniqueSessionId_IsBenign()
    {
        _h.Account();
        _h.Session();
        _h.Imports.ThrowOnNextSave = new Microsoft.EntityFrameworkCore.DbUpdateException("duplicate");

        await _h.Job().DiscoverAsync(CancellationToken.None);

        Assert.Equal(1, _h.Imports.ClearTrackingCount);
    }

    [Fact]
    public async Task Run_DiscoversAndStartsWorkingOnANewRowInTheSameRun()
    {
        _h.Account();
        var session = _h.Session();

        await RunAsync();

        var row = Assert.Single(_h.Imports.Imports);
        Assert.Equal(RecordingImportStatus.Waiting, row.STATUS);
        Assert.Single(_h.Recordings.FindCalls); // it was due at once and Google was asked
        Assert.Equal(session.SessionId, row.SESSION_ID);
    }

    // ---- Waiting: searching ------------------------------------------------------------------------------

    [Fact]
    public async Task Waiting_NothingFoundYet_StaysWaiting_AndLooksAgainAfterTheSearchBackoff()
    {
        var (context, import) = DueImport(); // first search was due 50 minutes ago

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Waiting, import.STATUS);
        Assert.Equal(Now + TimeSpan.FromMinutes(40), import.NEXT_ATTEMPT_AT_UTC); // polls fall at +0, +10, +30, +70: the next delay after +50 is 40 minutes
        Assert.Equal(0, import.ATTEMPTS);
        Assert.Equal(1, _h.Imports.SaveCount);

        var find = Assert.Single(_h.Recordings.FindCalls);
        Assert.Equal("access-token-2", find.AccessToken);
        Assert.Equal(RecordingJobHarness.MeetingCode, find.MeetingCode);
        Assert.Equal(context.EndsAtUtc - TimeSpan.FromHours(6), find.NotBeforeUtc);
        Assert.Equal(context.EndsAtUtc.AddHours(12), find.NotAfterUtc);
    }

    [Fact]
    public async Task Waiting_RecordingStartedOrEndedButNoFileYet_StaysWaiting()
    {
        var (_, import) = DueImport();
        _h.GoogleHasRecording(
            new MeetRecording("a", MeetRecordingState.Started, Now.AddHours(-2), null, null),
            new MeetRecording("b", MeetRecordingState.Ended, Now.AddHours(-2), Now.AddHours(-1), null));

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Waiting, import.STATUS);
        Assert.Empty(_h.Ingest.Calls);
    }

    [Fact]
    public async Task Waiting_GoogleHasNoRecordOfTheConference_IsAnAnswerNotAFailure()
    {
        var (_, import) = DueImport();
        _h.Recordings.OnFind = () => Result.Failure<IReadOnlyList<MeetRecording>>(GoogleErrors.NotFound("no such conference"));

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Waiting, import.STATUS);
        Assert.Equal(0, import.ATTEMPTS);
        Assert.Empty(_h.Alerts.RecordingImportFailedAlerts);
    }

    [Fact]
    public async Task Waiting_TheSearchWindowIsOver_EndsAsNoRecording_WithoutAnAlert_TheSessionRowSaysSo()
    {
        var (_, import) = DueImport(endedAgo: TimeSpan.FromHours(13)); // window = end + 12h, passed an hour ago

        await RunAsync();

        Assert.Equal(RecordingImportStatus.NoRecording, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.NoRecordingFound, import.ERROR_CODE);
        Assert.Equal(Now, import.COMPLETED_AT_UTC);
        Assert.Null(import.NEXT_ATTEMPT_AT_UTC);
        Assert.Empty(_h.Alerts.RecordingImportFailedAlerts); // a class nobody recorded is not an error: no e-mail (the contract alerts on Failed and NeedsReconnect)
        Assert.Empty(_h.Alerts.RecordingNeedsReconnectAlerts);
    }

    [Fact]
    public async Task Waiting_SeveralFinishedRecordings_TakesTheLongest()
    {
        DueImport();
        _h.GoogleHasRecording(
            RecordingJobHarness.Finished(Now.AddHours(-3), TimeSpan.FromMinutes(5), fileId: "short-file", name: "rec-short"),
            RecordingJobHarness.Finished(Now.AddHours(-2), TimeSpan.FromMinutes(50), fileId: "long-file", name: "rec-long"),
            RecordingJobHarness.Finished(Now.AddHours(-1), TimeSpan.FromMinutes(20), fileId: "mid-file", name: "rec-mid"));

        await RunAsync();

        Assert.Equal("long-file", Assert.Single(_h.Recordings.OpenCalls).DriveFileId);
    }

    [Fact]
    public void ChooseRecording_TiesGoToTheEarliestStart_AndUnfinishedOrFilelessOnesAreIgnored()
    {
        var early = RecordingJobHarness.Finished(Now.AddHours(-3), TimeSpan.FromMinutes(30), fileId: "early");
        var late = RecordingJobHarness.Finished(Now.AddHours(-2), TimeSpan.FromMinutes(30), fileId: "late");
        var noFile = new MeetRecording("n", MeetRecordingState.FileGenerated, Now.AddHours(-5), Now, DriveFileId: null);
        var running = new MeetRecording("r", MeetRecordingState.Started, Now.AddHours(-5), Now, "running");

        Assert.Equal("early", LiveRecordingImportJob.ChooseRecording([late, early, noFile, running])!.DriveFileId);
        Assert.Null(LiveRecordingImportJob.ChooseRecording([noFile, running]));
        Assert.Null(LiveRecordingImportJob.ChooseRecording([]));
    }

    // ---- Waiting -> Transferring -> Processing -----------------------------------------------------------------

    [Fact]
    public async Task Waiting_AFinishedRecording_IsClaimedThenCopiedToTheVideoProvider_AndWaitsForTranscoding()
    {
        var (context, import) = DueImport();
        var stream = new DisposalTrackingStream(1024);
        _h.GoogleHasRecording(RecordingJobHarness.Finished(Now.AddHours(-2), TimeSpan.FromMinutes(55)));
        _h.Recordings.OnOpen = () => Result.Success(new MeetRecordingDownload(stream, 1024, "rec.mp4", owner: null));

        await RunAsync();

        // The copy: opened with the instructor's token, streamed (never buffered whole), owned by the instructor, named after the class.
        var open = Assert.Single(_h.Recordings.OpenCalls);
        Assert.Equal("access-token-2", open.AccessToken);
        Assert.Equal(RecordingJobHarness.DriveFileId, open.DriveFileId);
        var ingest = Assert.Single(_h.Ingest.Calls);
        Assert.Equal(InstructorId, ingest.OwnerUserId);
        Assert.Equal($"บันทึก: {context.Title}", ingest.Title);
        Assert.Equal(1024, ingest.ContentLength);
        Assert.Equal(1024, _h.Ingest.BytesRead);
        Assert.True(stream.Disposed);

        // The row: claimed (one save), then Processing (a second save) — never both in one.
        Assert.Equal(2, _h.Imports.SaveCount);
        Assert.Equal(RecordingImportStatus.Processing, import.STATUS);
        Assert.Equal(_h.Ingest.LastAssetId, import.MEDIA_ASSET_ID);
        Assert.Equal(RecordingJobHarness.DriveFileId, import.GOOGLE_FILE_ID);
        Assert.Equal(RecordingJobHarness.RecordingName, import.GOOGLE_RECORDING_NAME);
        Assert.Equal(Now.AddMinutes(2), import.NEXT_ATTEMPT_AT_UTC);
        Assert.Equal(Now.AddHours(6), import.LEASE_UNTIL_UTC); // the transcode deadline
        Assert.Equal(0, import.ATTEMPTS);
        Assert.Empty(_h.Alerts.RecordingImportedAlerts); // not yet a lesson
    }

    [Fact]
    public async Task Transfer_TheClaimIsSavedBeforeTheFileIsTouched()
    {
        var (_, import) = DueImport();
        _h.GoogleHasRecording(RecordingJobHarness.Finished(Now.AddHours(-2), TimeSpan.FromMinutes(55)));
        RecordingImportStatus? statusWhenOpening = null;
        DateTime? leaseWhenOpening = null;
        var saveCountWhenOpening = -1;
        _h.Recordings.OnOpen = () =>
        {
            statusWhenOpening = import.STATUS;
            leaseWhenOpening = import.LEASE_UNTIL_UTC;
            saveCountWhenOpening = _h.Imports.SaveCount;
            return Result.Success(new MeetRecordingDownload(new MemoryStream(new byte[10]), 10, null, null));
        };

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Transferring, statusWhenOpening);
        Assert.Equal(Now.AddMinutes(180), leaseWhenOpening);
        Assert.Equal(1, saveCountWhenOpening);
    }

    [Fact]
    public async Task Transfer_DriveFileIsGone_EndsAsNoRecording()
    {
        var (_, import) = DueImport();
        _h.GoogleHasRecording(RecordingJobHarness.Finished(Now.AddHours(-2), TimeSpan.FromMinutes(55)));
        _h.Recordings.OnOpen = () => Result.Failure<MeetRecordingDownload>(GoogleErrors.NotFound("gone"));

        await RunAsync();

        Assert.Equal(RecordingImportStatus.NoRecording, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.DriveFileNotFound, import.ERROR_CODE);
        Assert.Empty(_h.Alerts.RecordingImportFailedAlerts);
        Assert.Empty(_h.Ingest.Calls);
    }

    [Fact]
    public async Task Transfer_DownloadFailsTransiently_GoesBackToWaiting_WithOneMoreAttemptAndABackoff()
    {
        var (_, import) = DueImport();
        _h.GoogleHasRecording(RecordingJobHarness.Finished(Now.AddHours(-2), TimeSpan.FromMinutes(55)));
        _h.Recordings.OnOpen = () => Result.Failure<MeetRecordingDownload>(GoogleErrors.RateLimited("slow down"));

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Waiting, import.STATUS);
        Assert.Equal(1, import.ATTEMPTS);
        Assert.Equal(RecordingImportErrorCodes.GoogleRateLimited, import.ERROR_CODE);
        Assert.Equal(Now + TimeSpan.FromMinutes(10), import.NEXT_ATTEMPT_AT_UTC);
        Assert.Null(import.LEASE_UNTIL_UTC);
        Assert.Equal(2, _h.Imports.SaveCount); // claim, then the failure
        Assert.Empty(_h.Alerts.RecordingImportFailedAlerts);
    }

    [Fact]
    public async Task Transfer_ATooLargeFileAnnouncedByGoogle_IsRefusedBeforeAnyByteIsCopied()
    {
        _h.ConfigureAutoImport = a => a.MaxFileSizeMegabytes = 1;
        var (context, import) = DueImport();
        var stream = new DisposalTrackingStream(16);
        _h.GoogleHasRecording(RecordingJobHarness.Finished(Now.AddHours(-2), TimeSpan.FromMinutes(55)));
        _h.Recordings.OnOpen = () => Result.Success(new MeetRecordingDownload(stream, 2 * 1024 * 1024, null, null));

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Failed, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.FileTooLarge, import.ERROR_CODE);
        Assert.Empty(_h.Ingest.Calls);
        Assert.True(stream.Disposed);
        Assert.Equal([(InstructorId, context.SessionId)], _h.Alerts.RecordingImportFailedAlerts);
    }

    [Fact]
    public async Task Transfer_ATooLargeFileWithNoAnnouncedLength_IsStoppedMidStream()
    {
        _h.ConfigureAutoImport = a => a.MaxFileSizeMegabytes = 1;
        var (_, import) = DueImport();
        _h.GoogleHasRecording(RecordingJobHarness.Finished(Now.AddHours(-2), TimeSpan.FromMinutes(55)));
        _h.Recordings.OnOpen = () => Result.Success(new MeetRecordingDownload(new DisposalTrackingStream(3 * 1024 * 1024), contentLength: null, null, null));

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Failed, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.FileTooLarge, import.ERROR_CODE);
        Assert.True(_h.Ingest.BytesRead <= 1024 * 1024); // never more than the limit went through
        Assert.Single(_h.Alerts.RecordingImportFailedAlerts);
    }

    [Fact]
    public async Task Transfer_TheVideoProviderRefusesTheFile_GoesBackToWaiting_WithOneMoreAttempt_AndTheContextIsWiped()
    {
        var (_, import) = DueImport();
        _h.GoogleHasRecording(RecordingJobHarness.Finished(Now.AddHours(-2), TimeSpan.FromMinutes(55)));
        _h.Ingest.Handler = _ => Task.FromResult(Result.Failure<Guid>(DomainError.Unavailable("video provider down")));

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Waiting, import.STATUS);
        Assert.Equal(1, import.ATTEMPTS);
        Assert.Equal(RecordingImportErrorCodes.IngestFailed, import.ERROR_CODE);
        Assert.Equal(Now + TimeSpan.FromMinutes(10), import.NEXT_ATTEMPT_AT_UTC);
        Assert.Null(import.MEDIA_ASSET_ID);
        Assert.Equal(1, _h.Imports.ClearTrackingCount); // Media may have left half-finished changes on the shared context
    }

    [Fact]
    public async Task Transfer_TheCopyThrows_IsARetryableFailure_NotACrash()
    {
        var (_, import) = DueImport();
        _h.GoogleHasRecording(RecordingJobHarness.Finished(Now.AddHours(-2), TimeSpan.FromMinutes(55)));
        _h.Ingest.Handler = _ => throw new HttpRequestException("connection reset by https://video.example.test/secret-video-id");

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Waiting, import.STATUS);
        Assert.Equal(1, import.ATTEMPTS);
        Assert.Equal(RecordingImportErrorCodes.IngestFailed, import.ERROR_CODE);
        Assert.DoesNotContain("secret-video-id", _h.JobLog.All); // only the exception TYPE is logged
        Assert.Contains(nameof(HttpRequestException), _h.JobLog.All);
    }

    [Fact]
    public async Task Transfer_FailingEveryTime_RunsOutOfAttempts_AndTheInstructorIsToldOnce()
    {
        _h.ConfigureAutoImport = a => a.MaxAttempts = 3;
        var (context, import) = DueImport();
        _h.GoogleHasRecording(RecordingJobHarness.Finished(Now.AddHours(-2), TimeSpan.FromMinutes(55)));
        _h.Ingest.Handler = _ => Task.FromResult(Result.Failure<Guid>(DomainError.Unavailable("down")));

        for (var i = 0; i < 4; i++)
        {
            await RunAsync();
            _h.Clock.UtcNow = _h.Clock.UtcNow.AddHours(2); // past every backoff
        }

        Assert.Equal(RecordingImportStatus.Failed, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.IngestFailed, import.ERROR_CODE);
        Assert.Equal(3, _h.Ingest.Calls.Count); // the fourth run found a finished row
        Assert.Equal([(InstructorId, context.SessionId)], _h.Alerts.RecordingImportFailedAlerts);
    }

    // ---- Transferring: the lease --------------------------------------------------------------------------------

    private SESSION_RECORDING_IMPORT TransferringImport(LiveSessionContext context, DateTime leaseUntil, int attempts = 0)
    {
        var import = _h.Import(context);
        import.BeginTransfer("rec-name", RecordingJobHarness.DriveFileId, leaseUntil);
        RecordingJobHarness.Force(import, nameof(SESSION_RECORDING_IMPORT.ATTEMPTS), attempts);
        return import;
    }

    [Fact]
    public async Task Lease_ATransferStillUnderItsLease_IsLeftAlone()
    {
        _h.Account();
        var context = _h.Session();
        var import = TransferringImport(context, leaseUntil: Now.AddMinutes(30));

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Transferring, import.STATUS);
        Assert.Empty(_h.Recordings.OpenCalls);
        Assert.Equal(0, _h.Imports.SaveCount);
    }

    [Fact]
    public async Task Lease_AnExpiredLease_IsReclaimed_CountsAnAttempt_AndTheTransferRestarts()
    {
        _h.Account();
        var context = _h.Session();
        var import = TransferringImport(context, leaseUntil: Now.AddMinutes(-1));

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Processing, import.STATUS);
        Assert.Single(_h.Recordings.OpenCalls);
        Assert.Single(_h.Ingest.Calls);
        Assert.Equal(0, import.ATTEMPTS); // reset by the successful copy
    }

    [Fact]
    public async Task Lease_AnExpiredLeaseOnTheLastAttempt_EndsAsFailed_WithoutTouchingTheFile()
    {
        _h.ConfigureAutoImport = a => a.MaxAttempts = 3;
        _h.Account();
        var context = _h.Session();
        var import = TransferringImport(context, leaseUntil: Now.AddMinutes(-1), attempts: 2);

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Failed, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.TransferTimeout, import.ERROR_CODE);
        Assert.Empty(_h.Recordings.OpenCalls);
        Assert.Equal([(InstructorId, context.SessionId)], _h.Alerts.RecordingImportFailedAlerts);
    }

    // ---- Processing -> Attached ----------------------------------------------------------------------------------

    [Fact]
    public async Task Processing_WhenTheAssetIsReady_AttachesTheLesson_AndTellsTheInstructor()
    {
        _h.Account();
        var context = _h.Session();
        var assetId = ReadyAsset();
        var import = ProcessingImport(context, assetId);
        var episodeId = Guid.NewGuid();
        _h.Attacher.OnAttach = () => Result.Success(new AttachedLiveRecording(episodeId));

        await RunAsync();

        var attach = Assert.Single(_h.Attacher.AttachCalls);
        Assert.Equal((InstructorId, context.CourseId, context.SessionId, assetId, (string?)null), attach); // title = the handler's own default
        Assert.Equal(RecordingImportStatus.Attached, import.STATUS);
        Assert.Equal(episodeId, import.EPISODE_ID);
        Assert.Equal(Now, import.COMPLETED_AT_UTC);
        Assert.Null(import.ERROR_CODE);
        Assert.Equal([(InstructorId, context.SessionId)], _h.Alerts.RecordingImportedAlerts);
        Assert.Equal(1, _h.Imports.SaveCount);
    }

    [Theory]
    [InlineData("Uploading", 0)]
    [InlineData("Processing", 0)]
    [InlineData("Ready", 0)] // ready but no duration yet
    public async Task Processing_WhenTheAssetIsNotReadyYet_LooksAgainInTwoMinutes(string status, int duration)
    {
        _h.Account();
        var context = _h.Session();
        var import = ProcessingImport(context, ReadyAsset(status: status, duration: duration == 0 ? null : duration));

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Processing, import.STATUS);
        Assert.Equal(Now.AddMinutes(2), import.NEXT_ATTEMPT_AT_UTC);
        Assert.Empty(_h.Attacher.AttachCalls);
        Assert.Empty(_h.Alerts.RecordingImportedAlerts);
    }

    [Fact]
    public async Task Processing_StillTranscodingAfterTheDeadline_Fails()
    {
        _h.Account();
        var context = _h.Session();
        var import = ProcessingImport(context, ReadyAsset(status: "Processing", duration: null), deadlineUtc: Now.AddSeconds(-1));

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Failed, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.TranscodeTimeout, import.ERROR_CODE);
        Assert.Equal([(InstructorId, context.SessionId)], _h.Alerts.RecordingImportFailedAlerts);
    }

    [Fact]
    public async Task Processing_TheVideoProviderFailedToTranscode_Fails()
    {
        _h.Account();
        var context = _h.Session();
        var import = ProcessingImport(context, ReadyAsset(status: "Failed", duration: null));

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Failed, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.TranscodeFailed, import.ERROR_CODE);
        Assert.Single(_h.Alerts.RecordingImportFailedAlerts);
    }

    [Fact]
    public async Task Processing_TheAssetHasVanished_Fails()
    {
        _h.Account();
        var context = _h.Session();
        var import = ProcessingImport(context, Guid.NewGuid()); // never added to the fake

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Failed, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.AssetMissing, import.ERROR_CODE);
    }

    [Fact]
    public async Task Processing_AttachSaysTheAssetIsNotReady_KeepsWaiting()
    {
        _h.Account();
        var context = _h.Session();
        var import = ProcessingImport(context, ReadyAsset());
        _h.Attacher.OnAttach = () => Result.Failure<AttachedLiveRecording>(DomainError.Conflict("not ready").WithReason(LiveRecordingAttachReasons.AssetNotReady));

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Processing, import.STATUS);
        Assert.Equal(Now.AddMinutes(2), import.NEXT_ATTEMPT_AT_UTC);
    }

    [Fact]
    public async Task Processing_AttachSaysTheAssetIsInUse_AndTheSessionNowHasARecording_IsSkipped()
    {
        _h.Account();
        var context = _h.Session();
        var import = ProcessingImport(context, ReadyAsset());
        _h.Attacher.OnAttach = () =>
        {
            // Someone attached a recording meanwhile (the instructor's own upload, or our own earlier attach whose save was lost).
            _h.Schedule.Contexts[0] = context with { RecordingEpisodeId = Guid.NewGuid() };
            return Result.Failure<AttachedLiveRecording>(DomainError.Conflict("in use").WithReason(LiveRecordingAttachReasons.AssetInUse));
        };

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Skipped, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.RecordingExists, import.ERROR_CODE);
        Assert.Empty(_h.Alerts.RecordingImportFailedAlerts);
    }

    [Fact]
    public async Task Processing_AttachSaysTheAssetIsInUse_ButTheSessionHasNoRecording_Fails()
    {
        _h.Account();
        var context = _h.Session();
        var import = ProcessingImport(context, ReadyAsset());
        _h.Attacher.OnAttach = () => Result.Failure<AttachedLiveRecording>(DomainError.Conflict("in use").WithReason(LiveRecordingAttachReasons.AssetInUse));

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Failed, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.AttachFailed, import.ERROR_CODE);
        Assert.Single(_h.Alerts.RecordingImportFailedAlerts);
    }

    [Fact]
    public async Task Processing_AttachSaysTheCourseOrSessionIsGone_IsSkipped()
    {
        _h.Account();
        var context = _h.Session();
        var import = ProcessingImport(context, ReadyAsset());
        _h.Attacher.OnAttach = () => Result.Failure<AttachedLiveRecording>(DomainError.NotFound("gone"));

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Skipped, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.SessionGone, import.ERROR_CODE);
    }

    [Fact]
    public async Task Processing_AttachIsRefused_IsFailed()
    {
        _h.Account();
        var context = _h.Session();
        var import = ProcessingImport(context, ReadyAsset());
        _h.Attacher.OnAttach = () => Result.Failure<AttachedLiveRecording>(DomainError.Forbidden("not yours"));

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Failed, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.AttachFailed, import.ERROR_CODE);
    }

    [Fact]
    public async Task Processing_AttachLosesAConcurrentEdit_IsRetriedLater_WithoutLeavingProcessing()
    {
        _h.Account();
        var context = _h.Session();
        var import = ProcessingImport(context, ReadyAsset());
        _h.Attacher.OnAttach = () => Result.Failure<AttachedLiveRecording>(DomainError.Conflict("edited elsewhere")); // no stable reason

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Processing, import.STATUS);
        Assert.Equal(1, import.ATTEMPTS);
        Assert.Equal(Now + TimeSpan.FromMinutes(10), import.NEXT_ATTEMPT_AT_UTC);
        Assert.Empty(_h.Alerts.RecordingImportFailedAlerts);
    }

    // ---- Re-reading the session first ------------------------------------------------------------------------------

    [Fact]
    public async Task Row_TheSessionNowHasARecordingLesson_IsSkipped_WithoutAskingGoogle()
    {
        _h.Account();
        var context = _h.Session();
        var import = _h.Import(context);
        _h.Schedule.Contexts[0] = context with { RecordingEpisodeId = Guid.NewGuid() }; // the instructor uploaded one by hand

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Skipped, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.RecordingExists, import.ERROR_CODE);
        Assert.Empty(_h.Recordings.FindCalls);
        Assert.Empty(_h.Alerts.RecordingImportFailedAlerts);
    }

    [Fact]
    public async Task Row_TheSessionWasCancelled_IsSkipped()
    {
        _h.Account();
        var context = _h.Session();
        var import = _h.Import(context);
        _h.Schedule.Contexts[0] = context with { Status = LiveSessionStatus.Cancelled };

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Skipped, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.SessionCancelled, import.ERROR_CODE);
    }

    [Fact]
    public async Task Row_TheSessionIsGone_IsSkipped()
    {
        _h.Account();
        var context = _h.Session();
        var import = _h.Import(context);
        _h.Schedule.Contexts.Clear();

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Skipped, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.SessionGone, import.ERROR_CODE);
    }

    // ---- The instructor's Google account ------------------------------------------------------------------------------

    [Fact]
    public async Task Waiting_NoAccountAnyMore_NeedsReconnect_AndTheInstructorIsTold()
    {
        var context = _h.Session();
        var import = _h.Import(context);

        await RunAsync();

        Assert.Equal(RecordingImportStatus.NeedsReconnect, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.GoogleAccountUnavailable, import.ERROR_CODE);
        Assert.Equal([(InstructorId, context.SessionId)], _h.Alerts.RecordingNeedsReconnectAlerts);
        Assert.Empty(_h.Recordings.FindCalls);
    }

    [Fact]
    public async Task Waiting_TheAccountWasRevoked_NeedsReconnect()
    {
        _h.Account().MarkRevoked(GoogleAccountRevokedReason.InvalidGrant, _h.Clock);
        var context = _h.Session();
        var import = _h.Import(context);

        await RunAsync();

        Assert.Equal(RecordingImportStatus.NeedsReconnect, import.STATUS);
        Assert.Single(_h.Alerts.RecordingNeedsReconnectAlerts);
    }

    [Fact]
    public async Task Waiting_TheAccountBecameAPersonalOne_IsSkipped_NobodyIsAlerted()
    {
        _h.Account(hostedDomain: null);
        var context = _h.Session();
        var import = _h.Import(context);

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Skipped, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.NotWorkspaceAccount, import.ERROR_CODE);
        Assert.Empty(_h.Recordings.FindCalls);
        Assert.Empty(_h.Alerts.RecordingNeedsReconnectAlerts);
    }

    [Fact]
    public async Task Waiting_TheRecordingScopesAreGone_NeedsReconnect_WithoutAskingGoogle()
    {
        _h.Account(scopes: GoogleScopes.CalendarEventsOwned);
        var context = _h.Session();
        var import = _h.Import(context);

        await RunAsync();

        Assert.Equal(RecordingImportStatus.NeedsReconnect, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.RecordingScopeMissing, import.ERROR_CODE);
        Assert.Empty(_h.Recordings.FindCalls);
        Assert.Single(_h.Alerts.RecordingNeedsReconnectAlerts);
    }

    [Fact]
    public async Task Waiting_AnAccountOfUnknownKind_IsLookedUpAndThenUsed()
    {
        _h.Account(checkedKind: false);
        _h.OAuth.UserInfoResult = Result.Success(new GoogleUserInfo("sub", "t@school.example.test", true, "school.example.test"));
        var context = _h.Session();
        _h.Import(context);

        await RunAsync();

        Assert.Single(_h.Recordings.FindCalls);
        Assert.Equal(GoogleAccountKind.Workspace, _h.Accounts.Accounts[0].AccountKind);
    }

    [Fact]
    public async Task Waiting_AnAccountWhoseKindCannotBeReadRightNow_IsATransientFailure_NotAVerdict()
    {
        _h.Account(checkedKind: false);
        _h.OAuth.UserInfoResult = Result.Failure<GoogleUserInfo>(GoogleErrors.Transient("down"));
        var context = _h.Session();
        var import = _h.Import(context);

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Waiting, import.STATUS);
        Assert.Equal(1, import.ATTEMPTS);
        Assert.Empty(_h.Recordings.FindCalls);
    }

    [Fact]
    public async Task Waiting_TheRoomIsNotAPlatformMadeGoogleRoom_IsSkipped()
    {
        _h.Account();
        var manual = _h.Session(roomProvider: MeetingProvider.Manual, roomUrl: "https://zoom.us/j/123");
        var none = _h.Session(withRoom: false);
        var importManual = _h.Import(manual);
        var importNone = _h.Import(none);

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Skipped, importManual.STATUS);
        Assert.Equal(RecordingImportStatus.Skipped, importNone.STATUS);
        Assert.Equal(RecordingImportErrorCodes.NoGoogleMeeting, importManual.ERROR_CODE);
        Assert.Empty(_h.Recordings.FindCalls);
    }

    [Fact]
    public async Task Waiting_TheRoomLinkIsNotAMeetCode_IsSkipped_AndNothingIsAskedOfGoogle()
    {
        _h.Account();
        var context = _h.Session(roomUrl: "https://meet.google.com/lookup/not-a-code");
        var import = _h.Import(context);

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Skipped, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.NoGoogleMeeting, import.ERROR_CODE);
        Assert.Empty(_h.Recordings.FindCalls);
    }

    [Fact]
    public async Task Waiting_TheRefreshTokenIsDead_NeedsReconnect()
    {
        DueImport();
        _h.OAuth.RefreshResult = Result.Failure<GoogleTokenSet>(GoogleErrors.Unauthorized("revoked", "invalid_grant"));

        await RunAsync();

        var row = Assert.Single(_h.Imports.Imports);
        Assert.Equal(RecordingImportStatus.NeedsReconnect, row.STATUS);
        Assert.Equal(RecordingImportErrorCodes.InvalidGrant, row.ERROR_CODE);
        Assert.Single(_h.Alerts.RecordingNeedsReconnectAlerts);
        Assert.Empty(_h.Recordings.FindCalls);
    }

    // ---- Google answers -------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Find_Unauthorized_NeedsReconnect()
    {
        var (_, import) = DueImport();
        _h.Recordings.OnFind = () => Result.Failure<IReadOnlyList<MeetRecording>>(GoogleErrors.Unauthorized("401"));

        await RunAsync();

        Assert.Equal(RecordingImportStatus.NeedsReconnect, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.InvalidGrant, import.ERROR_CODE);
    }

    [Fact]
    public async Task Find_Forbidden_MeansTheRecordingScopesAreMissing_NeedsReconnect()
    {
        var (context, import) = DueImport();
        _h.Recordings.OnFind = () => Result.Failure<IReadOnlyList<MeetRecording>>(GoogleErrors.Forbidden("403"));

        await RunAsync();

        Assert.Equal(RecordingImportStatus.NeedsReconnect, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.RecordingScopeMissing, import.ERROR_CODE);
        Assert.Equal([(InstructorId, context.SessionId)], _h.Alerts.RecordingNeedsReconnectAlerts);
    }

    [Fact]
    public async Task Find_BadRequest_FailsForGood()
    {
        var (_, import) = DueImport();
        _h.Recordings.OnFind = () => Result.Failure<IReadOnlyList<MeetRecording>>(GoogleErrors.BadRequest("400"));

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Failed, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.GoogleBadRequest, import.ERROR_CODE);
        Assert.Single(_h.Alerts.RecordingImportFailedAlerts);
    }

    [Theory]
    [InlineData(GoogleErrors.TransientCode, null, RecordingImportErrorCodes.GoogleTransient)]
    [InlineData(GoogleErrors.RateLimitedCode, null, RecordingImportErrorCodes.GoogleRateLimited)]
    [InlineData(GoogleErrors.TransientCode, "invalid_client", RecordingImportErrorCodes.GoogleClientMisconfigured)]
    [InlineData(GoogleErrors.TransientCode, InstructorGoogleAccountService.CredentialUnreadableReason, RecordingImportErrorCodes.GoogleClientMisconfigured)]
    [InlineData(GoogleErrors.NotConfiguredCode, null, RecordingImportErrorCodes.GoogleClientMisconfigured)]
    public void TransientCodeFor_MapsGoogleErrorsToStableCodes_NeverTheirMessage(string code, string? reason, string expected)
    {
        var error = new DomainError(code, "a message with an https://example.test/url and an id") { Reason = reason };

        Assert.Equal(expected, LiveRecordingImportJob.TransientCodeFor(error));
    }

    [Fact]
    public async Task Find_TransientFailures_BackOff10_20_40_Minutes_ThenFailAtTheConfiguredMaximum()
    {
        _h.ConfigureAutoImport = a => a.MaxAttempts = 4;
        var (context, import) = DueImport();
        _h.Recordings.OnFind = () => Result.Failure<IReadOnlyList<MeetRecording>>(GoogleErrors.Transient("503"));

        var expectedDelays = new[] { 10, 20, 40 };
        foreach (var minutes in expectedDelays)
        {
            await RunAsync();
            Assert.Equal(RecordingImportStatus.Waiting, import.STATUS);
            Assert.Equal(_h.Clock.UtcNow + TimeSpan.FromMinutes(minutes), import.NEXT_ATTEMPT_AT_UTC);
            _h.Clock.UtcNow = import.NEXT_ATTEMPT_AT_UTC!.Value;
        }

        Assert.Empty(_h.Alerts.RecordingImportFailedAlerts);

        await RunAsync();

        Assert.Equal(RecordingImportStatus.Failed, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.GoogleTransient, import.ERROR_CODE);
        Assert.Equal(4, import.ATTEMPTS);
        Assert.Equal([(InstructorId, context.SessionId)], _h.Alerts.RecordingImportFailedAlerts);
    }

    [Fact]
    public async Task Find_AnEmptyAnswerEndsTheRunOfFailures()
    {
        var (_, import) = DueImport();
        RecordingJobHarness.Force(import, nameof(SESSION_RECORDING_IMPORT.ATTEMPTS), 3);

        await RunAsync();

        Assert.Equal(0, import.ATTEMPTS);
    }

    // ---- Isolation, batching, concurrency ---------------------------------------------------------------------------------

    [Fact]
    public async Task Run_ProcessesAtMostBatchSizeRows()
    {
        _h.ConfigureAutoImport = a => a.BatchSize = 2;
        _h.Account();
        for (var i = 0; i < 3; i++)
        {
            _h.Import(_h.Session());
        }

        await RunAsync();

        Assert.Equal(2, _h.Recordings.FindCalls.Count);
    }

    [Fact]
    public async Task Run_ALostClaim_IsSkipped_NothingIsCopied_AndTheNextRowStillRuns()
    {
        _h.Account();
        var first = _h.Session(endedAgo: TimeSpan.FromHours(5));
        var second = _h.Session(endedAgo: TimeSpan.FromHours(2));
        _h.Import(first);
        _h.Import(second);
        _h.GoogleHasRecording(RecordingJobHarness.Finished(Now.AddHours(-6), TimeSpan.FromMinutes(55)));
        _h.Imports.ThrowOnNextSave = RecordingJobHarness.Conflict(); // the first row's claim

        await RunAsync();

        Assert.Equal(1, _h.Imports.ClearTrackingCount);
        Assert.Single(_h.Ingest.Calls); // only the second row copied: the first never got past its claim
    }

    [Fact]
    public async Task Run_AnUnexpectedExceptionInOneRow_CountsAnAttempt_AndNeverStopsTheRun()
    {
        _h.Account();
        var first = _h.Session(endedAgo: TimeSpan.FromHours(5));
        var second = _h.Session(endedAgo: TimeSpan.FromHours(2));
        var importFirst = _h.Import(first);
        var importSecond = _h.Import(second);
        var calls = 0;
        _h.Recordings.OnFind = () =>
        {
            if (++calls == 1)
            {
                throw new InvalidOperationException("secret detail https://example.test/leak");
            }

            return Result.Success<IReadOnlyList<MeetRecording>>([]);
        };

        await RunAsync();

        Assert.Equal(1, importFirst.ATTEMPTS);
        Assert.Equal(RecordingImportErrorCodes.InternalError, importFirst.ERROR_CODE);
        Assert.Equal(RecordingImportStatus.Waiting, importFirst.STATUS);
        Assert.Equal(2, calls); // the second row was still looked at
        Assert.NotNull(importSecond.NEXT_ATTEMPT_AT_UTC);
        Assert.DoesNotContain("secret detail", _h.JobLog.All);
        Assert.DoesNotContain("example.test/leak", _h.JobLog.All);
    }

    [Fact]
    public async Task Run_APoisonedRow_EventuallyFails_InsteadOfSpinningForever()
    {
        _h.ConfigureAutoImport = a => a.MaxAttempts = 2;
        var (context, import) = DueImport();
        _h.Recordings.ThrowOnFind = new InvalidOperationException("boom");

        await RunAsync();
        _h.Clock.UtcNow = _h.Clock.UtcNow.AddHours(2);
        await RunAsync();

        Assert.Equal(RecordingImportStatus.Failed, import.STATUS);
        Assert.Equal([(InstructorId, context.SessionId)], _h.Alerts.RecordingImportFailedAlerts);
    }

    [Fact]
    public async Task Run_ADiscoveryThatThrows_DoesNotStopTheRowsThatAreAlreadyDue()
    {
        var (_, import) = DueImport();
        _h.Attacher.Ended.Clear();
        var throwing = new ThrowingAttacher(_h.Attacher);
        var job = new LiveRecordingImportJob(
            _h.Imports, _h.Meetings, _h.Schedule, throwing, _h.Recordings, _h.Ingest, _h.Assets,
            new InstructorGoogleAccountService(
                _h.Accounts, _h.Meetings, _h.OAuth, new FakeStateStore(), _h.Protector, _h.Schedule, _h.Alerts, _h.Clock, _h.Options(),
                Microsoft.Extensions.Options.Options.Create(new GoogleOAuthOptions()), _h.ServiceLog),
            new SessionMeetingService(_h.Meetings, _h.Schedule, new FakeCatalog(), new MeetingLinkValidator(_h.Options()), _h.Protector, _h.Clock, new ListLogger<SessionMeetingService>()),
            _h.Alerts, _h.Scheduler, _h.Clock, _h.Options(), _h.JobLog);

        await job.RunAsync(CancellationToken.None);

        Assert.Single(_h.Recordings.FindCalls);
        Assert.NotNull(import.NEXT_ATTEMPT_AT_UTC);
    }

    // ---- The development fakes ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task LoggingMode_UsesTheStandInMeetCode_BecauseTheFakeRoomLinksAreNotMeetLinks()
    {
        _h.Mode = LiveProviderMode.Logging;
        _h.Account();
        var context = _h.Session(roomProvider: MeetingProvider.Logging, roomUrl: "https://meet.invalid/dev/some-room");
        _h.Import(context);

        await RunAsync();

        Assert.Equal(GoogleMeetCode.DevCode, Assert.Single(_h.Recordings.FindCalls).MeetingCode);
    }

    // ---- Nothing sensitive is ever logged ----------------------------------------------------------------------------------

    [Fact]
    public async Task NoLog_EverContainsATokenARoomLinkAMeetCodeADriveFileIdOrARecordingName()
    {
        // A little of everything: a full happy path, then failures of each kind.
        _h.Account();
        var ok = _h.Session(endedAgo: TimeSpan.FromHours(5));
        var bad = _h.Session(endedAgo: TimeSpan.FromHours(2));
        _h.Import(ok);
        _h.Import(bad);
        _h.GoogleHasRecording(RecordingJobHarness.Finished(Now.AddHours(-6), TimeSpan.FromMinutes(55)));
        var ingests = 0;
        _h.Ingest.Handler = stream =>
        {
            if (++ingests == 1)
            {
                return Task.FromResult(Result.Success(Guid.NewGuid()));
            }

            throw new HttpRequestException("failed for " + RecordingJobHarness.DriveFileId + " at " + RecordingJobHarness.RoomUrl);
        };

        await RunAsync();
        _h.Clock.UtcNow = _h.Clock.UtcNow.AddHours(3);
        _h.Recordings.OnFind = () => Result.Failure<IReadOnlyList<MeetRecording>>(GoogleErrors.Transient("503 for " + RecordingJobHarness.MeetingCode));
        await RunAsync();

        var logs = _h.JobLog.All + "\n" + _h.ServiceLog.All;
        foreach (var secret in new[]
        {
            "access-token-2", "refresh-token-1", RecordingJobHarness.RoomUrl, RecordingJobHarness.MeetingCode,
            RecordingJobHarness.DriveFileId, RecordingJobHarness.RecordingName, "CONF-SECRET", "REC-SECRET",
        })
        {
            Assert.DoesNotContain(secret, logs);
        }
    }

    /// <summary>A readable stream that remembers whether it was disposed.</summary>
    private sealed class DisposalTrackingStream(int length) : MemoryStream(new byte[length])
    {
        public bool Disposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            Disposed = true;
            base.Dispose(disposing);
        }
    }

    private sealed class ThrowingAttacher(ILiveRecordingAttacher inner) : ILiveRecordingAttacher
    {
        public Task<IReadOnlyList<EndedLiveSession>> ListEndedAsync(DateTime endedAfterUtc, DateTime endedBeforeUtc, int limit, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("catalog is down");

        public Task<IReadOnlyList<EndedLiveSession>> ListEndedByInstructorsAsync(
            IReadOnlyCollection<Guid> instructorUserIds, DateTime endedAfterUtc, DateTime endedBeforeUtc, int limit, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("catalog is down");

        public Task<EndedLiveSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken) => inner.GetAsync(sessionId, cancellationToken);

        public Task<Result<AttachedLiveRecording>> AttachAsync(
            Guid instructorUserId, Guid courseId, Guid sessionId, Guid mediaAssetId, string? episodeTitle, CancellationToken cancellationToken) =>
            inner.AttachAsync(instructorUserId, courseId, sessionId, mediaAssetId, episodeTitle, cancellationToken);
    }
}
