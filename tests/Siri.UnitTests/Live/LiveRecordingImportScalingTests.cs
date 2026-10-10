using System.Reflection;
using Hangfire;
using Hangfire.Common;
using Hangfire.Server;
using Hangfire.States;
using Siri.Integrations.Google;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.Modules.Live.Infrastructure;
using Siri.SharedKernel;

namespace Siri.UnitTests.Live;

/// <summary>
/// Two scaling problems of the P11-13 import that the first version had, and the guarantees that replaced them:
/// <list type="bullet">
/// <item><b>Discovery cannot starve.</b> Catalog lists oldest first with a limit; sessions that never get an import row (instructors on the manual path, sessions
/// without a Google room) used to be re-listed on every run, so past one page of them a Workspace instructor's newer session was never reached. Now only instructors
/// who could be imported at all are asked about, and the window is paged until it is exhausted (hard cap).</item>
/// <item><b>The five-minute tick is short.</b> It claims a found recording and queues a separate background job for the long copy; a transfer outliving the tick can no
/// longer make the next triggers wait for a lock and fail.</item>
/// </list>
/// </summary>
public class LiveRecordingImportScalingTests
{
    private static readonly DateTime Now = LiveTestData.Now;
    private static readonly Guid InstructorId = RecordingJobHarness.InstructorId;

    private readonly RecordingJobHarness _h = new();

    // ---- F1: discovery cannot starve -----------------------------------------------------------------------------

    [Fact]
    public async Task Discovery_AWorkspaceSession_IsStillFound_WhenMoreThanTwoHundredSessionsOfOtherInstructorsPrecedeIt()
    {
        _h.Account();

        // 250 older sessions of instructors on the manual path (no automatic-import account at all)...
        for (var i = 0; i < 250; i++)
        {
            _h.Session(endedAgo: TimeSpan.FromHours(40) - TimeSpan.FromMinutes(i), instructorUserId: Guid.NewGuid());
        }

        // ...and then the Workspace instructor's session, which ended later than all of them.
        var wanted = _h.Session(endedAgo: TimeSpan.FromHours(1));
        Assert.True(_h.Attacher.Ended.Count(s => s.EndsAtUtc < wanted.EndsAtUtc) > LiveRecordingImportJob.DiscoveryPageSize);

        await _h.Job().DiscoverAsync(CancellationToken.None);

        var row = Assert.Single(_h.Imports.Imports);
        Assert.Equal(wanted.SessionId, row.SESSION_ID);

        // Catalog was asked only about the one instructor who can be imported - never about the 250 others.
        Assert.All(_h.Attacher.ListByInstructorCalls, call => Assert.Equal([InstructorId], call.InstructorUserIds));
    }

    [Fact]
    public async Task Discovery_PagesThroughAWindowLongerThanOnePage_UntilAShortPageSaysItIsExhausted()
    {
        _h.Account();

        // 450 sessions of the Workspace instructor that can never get a row (no Google room), 3 minutes apart, then one with a room that ended last.
        for (var i = 0; i < 450; i++)
        {
            _h.Session(endedAgo: TimeSpan.FromHours(47) - TimeSpan.FromMinutes(3 * i), withRoom: false);
        }

        var wanted = _h.Session(endedAgo: TimeSpan.FromHours(1));

        await _h.Job().DiscoverAsync(CancellationToken.None);

        Assert.Equal(wanted.SessionId, Assert.Single(_h.Imports.Imports).SESSION_ID);
        Assert.Equal(3, _h.Attacher.ListByInstructorCalls.Count); // 200 + 200 + the remaining 52: a short page ends it
        Assert.All(_h.Attacher.ListByInstructorCalls, call => Assert.Equal(LiveRecordingImportJob.DiscoveryPageSize, call.Limit));
    }

    [Fact]
    public async Task Discovery_TheCursorAdvances_ByTheLastSessionsEndTime()
    {
        _h.Account();
        for (var i = 0; i < 210; i++)
        {
            _h.Session(endedAgo: TimeSpan.FromHours(30) - TimeSpan.FromMinutes(i), withRoom: false);
        }

        await _h.Job().DiscoverAsync(CancellationToken.None);

        var first = _h.Attacher.ListByInstructorCalls[0];
        var second = _h.Attacher.ListByInstructorCalls[1];
        Assert.Equal(Now - LiveRecordingImportJob.DiscoveryLookback, first.After);
        Assert.Equal(first.Before, second.Before);
        Assert.True(second.After > first.After);
        Assert.Equal(_h.Attacher.Ended.OrderBy(s => s.EndsAtUtc).ThenBy(s => s.SessionId).ElementAt(199).EndsAtUtc, second.After);
    }

    [Fact]
    public async Task Discovery_AHardCapBoundsTheWork_HoweverManySessionsTheWindowHolds()
    {
        _h.Account();
        var perPage = LiveRecordingImportJob.DiscoveryPageSize;
        for (var i = 0; i < (perPage * LiveRecordingImportJob.MaxDiscoveryPages) + 150; i++)
        {
            _h.Session(endedAgo: TimeSpan.FromHours(47) - TimeSpan.FromSeconds(30 * i), withRoom: false);
        }

        await _h.Job().DiscoverAsync(CancellationToken.None);

        Assert.Equal(LiveRecordingImportJob.MaxDiscoveryPages, _h.Attacher.ListByInstructorCalls.Count);
    }

    [Fact]
    public async Task Discovery_ManySessionsEndingInTheSameInstant_CannotMakeItLoopForever()
    {
        _h.Account();
        for (var i = 0; i < 250; i++)
        {
            _h.Session(endedAgo: TimeSpan.FromHours(5), withRoom: false);
        }

        await _h.Job().DiscoverAsync(CancellationToken.None);

        Assert.InRange(_h.Attacher.ListByInstructorCalls.Count, 1, LiveRecordingImportJob.MaxDiscoveryPages);
    }

    [Fact]
    public async Task Discovery_SeesEachSessionOnce_EvenThoughTheCursorIsInclusive()
    {
        _h.Account();
        for (var i = 0; i < 230; i++)
        {
            _h.Session(endedAgo: TimeSpan.FromHours(30) - TimeSpan.FromMinutes(i));
        }

        await _h.Job().DiscoverAsync(CancellationToken.None);

        Assert.Equal(230, _h.Imports.Imports.Count);
        Assert.Equal(230, _h.Imports.Imports.Select(i => i.SESSION_ID).Distinct().Count());
        Assert.Equal(1, _h.Imports.SaveCount);
    }

    // ---- F2: the tick claims and queues; it never copies ---------------------------------------------------------

    private (LiveSessionContext Context, SESSION_RECORDING_IMPORT Import) DueImportWithARecordingOnGoogle()
    {
        _h.Account();
        var context = _h.Session();
        var import = _h.Import(context);
        _h.GoogleHasRecording(RecordingJobHarness.Finished(Now.AddHours(-2), TimeSpan.FromMinutes(55)));
        return (context, import);
    }

    [Fact]
    public async Task Tick_ClaimsAFoundRecording_AndQueuesExactlyOneTransfer_WithoutCopyingAnything()
    {
        var (_, import) = DueImportWithARecordingOnGoogle();

        await _h.RunTickAsync();

        Assert.Equal(RecordingImportStatus.Transferring, import.STATUS);
        Assert.Equal(Now.AddMinutes(180), import.LEASE_UNTIL_UTC);
        Assert.Equal([import.SESSION_RECORDING_IMPORT_ID], _h.Scheduler.Enqueued);
        Assert.Empty(_h.Recordings.OpenCalls); // the file was not touched by the tick
        Assert.Empty(_h.Ingest.Calls);
        Assert.Equal(1, _h.Imports.SaveCount); // just the claim
    }

    [Fact]
    public async Task Tick_NeverQueuesTheSameClaimTwice_NoMatterHowManyTimesItRuns()
    {
        var (_, import) = DueImportWithARecordingOnGoogle();

        for (var i = 0; i < 4; i++)
        {
            await _h.RunTickAsync();
            _h.Clock.UtcNow = _h.Clock.UtcNow.AddMinutes(5); // each tick is five minutes later; the lease runs for three hours
        }

        Assert.Equal([import.SESSION_RECORDING_IMPORT_ID], _h.Scheduler.Enqueued);
        Assert.Single(_h.Recordings.FindCalls); // and Google was asked once: the later ticks found nothing due
    }

    [Fact]
    public async Task Tick_ALostClaim_QueuesNothing()
    {
        DueImportWithARecordingOnGoogle();
        _h.Imports.ThrowOnNextSave = RecordingJobHarness.Conflict();

        await _h.RunTickAsync();

        Assert.Empty(_h.Scheduler.Enqueued);
        Assert.Equal(1, _h.Imports.ClearTrackingCount);
    }

    [Fact]
    public async Task ASecondTick_WhileATransferIsStillRunning_NeitherBlocksNorFails_AndDoesNotQueueAgain()
    {
        var (_, import) = DueImportWithARecordingOnGoogle();
        await _h.RunTickAsync();
        var queued = Assert.Single(_h.Scheduler.Enqueued);

        // The transfer job is now "running": it has opened the file and is stuck inside the copy.
        var copyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCopy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _h.Ingest.Handler = async stream =>
        {
            copyStarted.SetResult();
            await finishCopy.Task;
            return Result.Success(Guid.NewGuid());
        };

        var transfer = _h.TransferJob().RunAsync(queued, CancellationToken.None);
        await copyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.False(transfer.IsCompleted);

        // Several more five-minute ticks arrive while it runs. Each completes at once, throws nothing, and queues nothing.
        for (var i = 0; i < 3; i++)
        {
            _h.Clock.UtcNow = _h.Clock.UtcNow.AddMinutes(5);
            await _h.RunTickAsync().WaitAsync(TimeSpan.FromSeconds(5));
        }

        Assert.Equal([queued], _h.Scheduler.Enqueued);
        Assert.Equal(RecordingImportStatus.Transferring, import.STATUS); // still the transfer's: the ticks did not touch it
        Assert.False(transfer.IsCompleted);

        finishCopy.SetResult();
        await transfer.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RecordingImportStatus.Processing, import.STATUS);
    }

    [Fact]
    public async Task ATickThatHasOtherRowsToDo_StillDoesThem_WhileAnotherRowsTransferRuns()
    {
        var (_, first) = DueImportWithARecordingOnGoogle();
        await _h.RunTickAsync();
        var copyStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var finishCopy = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _h.Ingest.Handler = async _ =>
        {
            copyStarted.SetResult();
            await finishCopy.Task;
            return Result.Success(Guid.NewGuid());
        };
        var transfer = _h.TransferJob().RunAsync(first.SESSION_RECORDING_IMPORT_ID, CancellationToken.None);
        await copyStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

        // Another class ended meanwhile; Google has no recording for it yet.
        var second = _h.Import(_h.Session(endedAgo: TimeSpan.FromHours(2)));
        _h.GoogleHasRecording();

        await _h.RunTickAsync().WaitAsync(TimeSpan.FromSeconds(5));

        Assert.Equal(RecordingImportStatus.Waiting, second.STATUS);
        Assert.NotNull(second.NEXT_ATTEMPT_AT_UTC); // searched and scheduled its next look while the first copy was still running

        finishCopy.SetResult();
        await transfer.WaitAsync(TimeSpan.FromSeconds(5));
    }

    [Fact]
    public async Task Tick_ReclaimsAnExpiredLease_CountsAnAttempt_AndQueuesTheTransferAgain()
    {
        _h.Account();
        var context = _h.Session();
        var import = _h.Import(context);
        import.BeginTransfer("rec-name", RecordingJobHarness.DriveFileId, Now.AddMinutes(-1)); // the job that held the lease is gone

        await _h.RunTickAsync();

        Assert.Equal(RecordingImportStatus.Transferring, import.STATUS);
        Assert.Equal(1, import.ATTEMPTS);
        Assert.Equal(Now.AddMinutes(180), import.LEASE_UNTIL_UTC);
        Assert.Equal([import.SESSION_RECORDING_IMPORT_ID], _h.Scheduler.Enqueued);
        Assert.Empty(_h.Recordings.OpenCalls); // still the tick: nothing was copied by it
        Assert.Equal(1, _h.Imports.SaveCount);

        // ...and the queued job then does the copy.
        await _h.DrainTransfersAsync();
        Assert.Equal(RecordingImportStatus.Processing, import.STATUS);
    }

    [Fact]
    public async Task Tick_AnExpiredLeaseOnTheLastAttempt_FailsTheImport_AndQueuesNothing()
    {
        _h.ConfigureAutoImport = a => a.MaxAttempts = 2;
        _h.Account();
        var context = _h.Session();
        var import = _h.Import(context);
        import.BeginTransfer("rec-name", RecordingJobHarness.DriveFileId, Now.AddMinutes(-1));
        RecordingJobHarness.Force(import, nameof(SESSION_RECORDING_IMPORT.ATTEMPTS), 1);

        await _h.RunTickAsync();

        Assert.Equal(RecordingImportStatus.Failed, import.STATUS);
        Assert.Equal(RecordingImportErrorCodes.TransferTimeout, import.ERROR_CODE);
        Assert.Empty(_h.Scheduler.Enqueued);
        Assert.Equal([(InstructorId, context.SessionId)], _h.Alerts.RecordingImportFailedAlerts);
    }

    [Fact]
    public async Task Tick_ATransferWhoseJobWasNeverQueued_IsRecoveredByTheLease_ExactlyAsBefore()
    {
        // The process died between saving the claim and queueing the job: the row sits in Transferring under a lease, with no job anywhere.
        _h.Account();
        var context = _h.Session();
        var import = _h.Import(context);
        import.BeginTransfer("rec-name", RecordingJobHarness.DriveFileId, Now.AddMinutes(180));

        await _h.RunTickAsync();
        Assert.Empty(_h.Scheduler.Enqueued); // under its lease: left alone

        _h.Clock.UtcNow = Now.AddMinutes(181);
        await _h.RunTickAsync();

        Assert.Equal([import.SESSION_RECORDING_IMPORT_ID], _h.Scheduler.Enqueued);
        Assert.Equal(1, import.ATTEMPTS);
    }

    [Fact]
    public async Task Tick_WhenTheTransferCannotBeQueued_GivesTheRowBackAtOnce_InsteadOfWaitingOutItsLease()
    {
        var (_, import) = DueImportWithARecordingOnGoogle();
        _h.Scheduler.ThrowOnEnqueue = new InvalidOperationException("hangfire storage down at https://secret-host.example.test");

        await _h.RunTickAsync(); // must not throw

        Assert.Equal(RecordingImportStatus.Waiting, import.STATUS);
        Assert.Equal(1, import.ATTEMPTS);
        Assert.Null(import.LEASE_UNTIL_UTC);
        Assert.Equal(Now + TimeSpan.FromMinutes(10), import.NEXT_ATTEMPT_AT_UTC);
        Assert.DoesNotContain("secret-host", _h.JobLog.All);
    }

    // ---- The transfer job's own guards ---------------------------------------------------------------------------------

    private async Task<(SESSION_RECORDING_IMPORT Import, Guid Id)> ClaimedAsync()
    {
        var (_, import) = DueImportWithARecordingOnGoogle();
        await _h.RunTickAsync();
        return (import, import.SESSION_RECORDING_IMPORT_ID);
    }

    [Fact]
    public async Task Transfer_RunsTheWholeCopy_AndLeavesTheRowWaitingForTranscoding()
    {
        var (import, id) = await ClaimedAsync();

        await _h.TransferJob().RunAsync(id, CancellationToken.None);

        Assert.Equal(RecordingImportStatus.Processing, import.STATUS);
        Assert.Equal(_h.Ingest.LastAssetId, import.MEDIA_ASSET_ID);
        Assert.Single(_h.Ingest.Calls);
    }

    [Fact]
    public async Task Transfer_ARowThatIsNoLongerTransferring_IsLeftAlone()
    {
        var (import, id) = await ClaimedAsync();
        await _h.TransferJob().RunAsync(id, CancellationToken.None); // finished: now Processing

        await _h.TransferJob().RunAsync(id, CancellationToken.None); // a duplicate job for the same import

        Assert.Single(_h.Ingest.Calls);
        Assert.Equal(RecordingImportStatus.Processing, import.STATUS);
    }

    [Fact]
    public async Task Transfer_AnUnknownImport_IsANoOp()
    {
        await _h.TransferJob().RunAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(_h.Ingest.Calls);
        Assert.Equal(0, _h.Imports.SaveCount);
    }

    [Fact]
    public async Task Transfer_StartingAfterTheLeaseRanOut_DoesNothing_TheTickReclaimsIt()
    {
        var (import, id) = await ClaimedAsync();
        _h.Clock.UtcNow = Now.AddMinutes(181);

        await _h.TransferJob().RunAsync(id, CancellationToken.None);

        Assert.Empty(_h.Ingest.Calls);
        Assert.Equal(RecordingImportStatus.Transferring, import.STATUS);
    }

    [Fact]
    public async Task Transfer_TheSessionNowHasARecordingLesson_IsSkipped_NothingIsCopied()
    {
        var (import, id) = await ClaimedAsync();
        _h.Schedule.Contexts[0] = _h.Schedule.Contexts[0] with { RecordingEpisodeId = Guid.NewGuid() };

        await _h.TransferJob().RunAsync(id, CancellationToken.None);

        Assert.Equal(RecordingImportStatus.Skipped, import.STATUS);
        Assert.Empty(_h.Recordings.OpenCalls);
    }

    [Fact]
    public async Task Transfer_TheFeatureWasSwitchedOffMeanwhile_DoesNothing()
    {
        var (import, id) = await ClaimedAsync();
        _h.Enabled = false;

        await _h.TransferJob().RunAsync(id, CancellationToken.None);

        Assert.Empty(_h.Recordings.OpenCalls);
        Assert.Equal(RecordingImportStatus.Transferring, import.STATUS);
    }

    [Fact]
    public async Task Transfer_TheInstructorsTokenIsDeadByNow_NeedsReconnect()
    {
        var (import, id) = await ClaimedAsync();
        _h.OAuth.RefreshResult = Result.Failure<GoogleTokenSet>(GoogleErrors.Unauthorized("revoked", "invalid_grant"));

        await _h.TransferJob().RunAsync(id, CancellationToken.None);

        Assert.Equal(RecordingImportStatus.NeedsReconnect, import.STATUS);
        Assert.Single(_h.Alerts.RecordingNeedsReconnectAlerts);
    }

    [Fact]
    public async Task Transfer_AnUnexpectedExceptionNeverEscapes_ItCountsAnAttemptAndGivesTheRowBack()
    {
        var (import, id) = await ClaimedAsync();
        _h.Recordings.OnOpen = () => throw new InvalidOperationException("boom https://example.test/leak");

        await _h.TransferJob().RunAsync(id, CancellationToken.None); // must not throw: Hangfire would only mark a failed job

        Assert.Equal(RecordingImportStatus.Waiting, import.STATUS);
        Assert.Equal(1, import.ATTEMPTS);
        Assert.Equal(RecordingImportErrorCodes.InternalError, import.ERROR_CODE);
        Assert.DoesNotContain("example.test/leak", _h.JobLog.All);
    }

    // ---- Hangfire wiring -------------------------------------------------------------------------------------------------

    [Fact]
    public void TransferJob_HasNoAutomaticRetry_AndAPerImportStorageLock_WithAGenerousTimeout()
    {
        var method = typeof(LiveRecordingTransferJob).GetMethod(nameof(LiveRecordingTransferJob.RunAsync))!;

        Assert.Equal(0, method.GetCustomAttribute<AutomaticRetryAttribute>()!.Attempts); // the row's state machine owns retries
        Assert.NotNull(method.GetCustomAttribute<DisableConcurrentTransferPerImportAttribute>());
        Assert.Null(method.GetCustomAttribute<DisableConcurrentExecutionAttribute>()); // the stock attribute locks per METHOD: it would serialize every import
        Assert.True(LiveRecordingTransferJob.LockTimeoutSeconds >= 600);
        Assert.True(typeof(IServerFilter).IsAssignableFrom(typeof(DisableConcurrentTransferPerImportAttribute)));
    }

    [Fact]
    public void TheTransferLock_IsPerImport_SoDifferentImportsRunSideBySide_AndTheSameOneNeverTwice()
    {
        var one = Guid.NewGuid();
        var other = Guid.NewGuid();

        var jobOne = Job.FromExpression<LiveRecordingTransferJob>(job => job.RunAsync(one, CancellationToken.None));
        var jobOneAgain = Job.FromExpression<LiveRecordingTransferJob>(job => job.RunAsync(one, CancellationToken.None));
        var jobOther = Job.FromExpression<LiveRecordingTransferJob>(job => job.RunAsync(other, CancellationToken.None));

        Assert.Equal(DisableConcurrentTransferPerImportAttribute.ResourceFor(jobOne), DisableConcurrentTransferPerImportAttribute.ResourceFor(jobOneAgain));
        Assert.NotEqual(DisableConcurrentTransferPerImportAttribute.ResourceFor(jobOne), DisableConcurrentTransferPerImportAttribute.ResourceFor(jobOther));
        Assert.Contains(one.ToString("N"), DisableConcurrentTransferPerImportAttribute.ResourceFor(jobOne));
    }

    [Fact]
    public void TheTick_StopsStartingRowsBeforeItsOwnStorageLockWouldTimeOutTheNextTrigger()
    {
        var method = typeof(LiveRecordingImportJob).GetMethod(nameof(LiveRecordingImportJob.RunAsync))!;

        Assert.NotNull(method.GetCustomAttribute<DisableConcurrentExecutionAttribute>());
        Assert.True(
            LiveRecordingImportJob.MaxRunDuration < TimeSpan.FromSeconds(LiveRecordingImportJob.LockTimeoutSeconds),
            "a tick that is allowed to run longer than the lock timeout makes the next trigger fail instead of wait");
    }

    [Fact]
    public void Scheduler_QueuesOneFireAndForgetBackgroundJob_CarryingOnlyTheImportId()
    {
        var client = new RecordingBackgroundJobClient();
        var scheduler = new HangfireRecordingTransferScheduler(client);
        var importId = Guid.NewGuid();

        scheduler.Enqueue(importId);

        var created = Assert.Single(client.Created);
        Assert.Equal(typeof(LiveRecordingTransferJob), created.Job.Type);
        Assert.Equal(nameof(LiveRecordingTransferJob.RunAsync), created.Job.Method.Name);
        Assert.Equal(importId, created.Job.Args[0]);
        Assert.IsType<EnqueuedState>(created.State);
        Assert.Throws<ArgumentException>(() => scheduler.Enqueue(Guid.Empty));
    }

    private sealed class RecordingBackgroundJobClient : IBackgroundJobClient
    {
        public List<(Job Job, IState State)> Created { get; } = [];

        public string Create(Job job, IState state)
        {
            Created.Add((job, state));
            return Guid.NewGuid().ToString("N");
        }

        public bool ChangeState(string jobId, IState state, string expectedState) => true;
    }
}
