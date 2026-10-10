using Siri.Modules.Live.Domain;

namespace Siri.UnitTests.Live;

/// <summary>The import row's state machine (docs/contracts/P11-13-live-recording-auto-import.md section 3 and 6): what each transition sets and clears, the guards that make an
/// impossible transition a bug rather than a silent state change, and the retry reset.</summary>
public class SessionRecordingImportTests
{
    private static readonly DateTime Now = LiveTestData.Now;

    private readonly FakeClock _clock = new(Now);

    private SESSION_RECORDING_IMPORT Waiting() =>
        SESSION_RECORDING_IMPORT.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), firstSearchAtUtc: Now, searchUntilUtc: Now.AddHours(12));

    private SESSION_RECORDING_IMPORT Transferring()
    {
        var import = Waiting();
        import.BeginTransfer("conferenceRecords/c/recordings/r", "file-1", Now.AddHours(3));
        return import;
    }

    private SESSION_RECORDING_IMPORT Processing()
    {
        var import = Transferring();
        import.MarkProcessing(Guid.NewGuid(), Now.AddMinutes(2), Now.AddHours(6));
        return import;
    }

    // ---- Create --------------------------------------------------------------------------------------

    [Fact]
    public void Create_StartsWaiting_DueAtTheFirstSearch_WithNothingElseSet()
    {
        var session = Guid.NewGuid();
        var course = Guid.NewGuid();
        var instructor = Guid.NewGuid();

        var import = SESSION_RECORDING_IMPORT.Create(session, course, instructor, Now, Now.AddHours(12));

        Assert.NotEqual(Guid.Empty, import.SESSION_RECORDING_IMPORT_ID);
        Assert.Equal((session, course, instructor), (import.SESSION_ID, import.COURSE_ID, import.INSTRUCTOR_USER_ID));
        Assert.Equal(RecordingImportStatus.Waiting, import.STATUS);
        Assert.Equal(0, import.ATTEMPTS);
        Assert.Equal(Now, import.NEXT_ATTEMPT_AT_UTC);
        Assert.Equal(Now.AddHours(12), import.SEARCH_UNTIL_UTC);
        Assert.Null(import.LEASE_UNTIL_UTC);
        Assert.Null(import.GOOGLE_FILE_ID);
        Assert.Null(import.MEDIA_ASSET_ID);
        Assert.Null(import.EPISODE_ID);
        Assert.Null(import.ERROR_CODE);
        Assert.Null(import.COMPLETED_AT_UTC);
        Assert.False(import.IsTerminal);
        Assert.False(import.CanRetry);
    }

    [Fact]
    public void Create_RejectsEmptyIds()
    {
        Assert.Throws<ArgumentException>(() => SESSION_RECORDING_IMPORT.Create(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Now, Now));
        Assert.Throws<ArgumentException>(() => SESSION_RECORDING_IMPORT.Create(Guid.NewGuid(), Guid.Empty, Guid.NewGuid(), Now, Now));
        Assert.Throws<ArgumentException>(() => SESSION_RECORDING_IMPORT.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.Empty, Now, Now));
    }

    // ---- Waiting -------------------------------------------------------------------------------------

    [Fact]
    public void ScheduleNextSearch_StaysWaiting_ClearsTheRunOfFailures()
    {
        var import = Waiting();
        import.RecordTransientFailure("google_transient", TimeSpan.FromMinutes(10), maxAttempts: 6, _clock);

        import.ScheduleNextSearch(Now.AddMinutes(40));

        Assert.Equal(RecordingImportStatus.Waiting, import.STATUS);
        Assert.Equal(0, import.ATTEMPTS);
        Assert.Equal(Now.AddMinutes(40), import.NEXT_ATTEMPT_AT_UTC);
        Assert.Null(import.ERROR_CODE);
    }

    [Fact]
    public void BeginTransfer_TakesTheLease_RemembersTheRecording_AndKeepsTheAttempts()
    {
        var import = Waiting();
        import.RecordTransientFailure("google_transient", TimeSpan.FromMinutes(10), maxAttempts: 6, _clock);

        import.BeginTransfer("conferenceRecords/c/recordings/r", "file-1", Now.AddHours(3));

        Assert.Equal(RecordingImportStatus.Transferring, import.STATUS);
        Assert.Equal("conferenceRecords/c/recordings/r", import.GOOGLE_RECORDING_NAME);
        Assert.Equal("file-1", import.GOOGLE_FILE_ID);
        Assert.Equal(Now.AddHours(3), import.LEASE_UNTIL_UTC);
        Assert.Null(import.NEXT_ATTEMPT_AT_UTC);
        Assert.Equal(1, import.ATTEMPTS); // a transfer that keeps failing must still run out of attempts
    }

    [Fact]
    public void BeginTransfer_RejectsBlankOrOverlongGoogleIds()
    {
        Assert.Throws<ArgumentException>(() => Waiting().BeginTransfer(" ", "file", Now));
        Assert.Throws<ArgumentException>(() => Waiting().BeginTransfer("name", "", Now));
        Assert.Throws<ArgumentException>(() => Waiting().BeginTransfer(new string('n', 201), "file", Now));
        Assert.Throws<ArgumentException>(() => Waiting().BeginTransfer("name", new string('f', 201), Now));
    }

    [Fact]
    public void BeginTransfer_OnlyFromWaiting()
    {
        Assert.Throws<InvalidOperationException>(() => Transferring().BeginTransfer("n", "f", Now));
        Assert.Throws<InvalidOperationException>(() => Processing().BeginTransfer("n", "f", Now));
    }

    // ---- Transferring --------------------------------------------------------------------------------

    [Fact]
    public void MarkProcessing_StoresTheAsset_ResetsAttempts_AndKeepsTheTranscodeDeadlineInTheLeaseColumn()
    {
        var import = Transferring();
        var asset = Guid.NewGuid();

        import.MarkProcessing(asset, Now.AddMinutes(2), Now.AddHours(6));

        Assert.Equal(RecordingImportStatus.Processing, import.STATUS);
        Assert.Equal(asset, import.MEDIA_ASSET_ID);
        Assert.Equal(0, import.ATTEMPTS);
        Assert.Equal(Now.AddMinutes(2), import.NEXT_ATTEMPT_AT_UTC);
        Assert.Equal(Now.AddHours(6), import.LEASE_UNTIL_UTC);
    }

    [Fact]
    public void MarkProcessing_NeedsATransferInProgress_AndARealAsset()
    {
        Assert.Throws<InvalidOperationException>(() => Waiting().MarkProcessing(Guid.NewGuid(), Now, Now));
        Assert.Throws<ArgumentException>(() => Transferring().MarkProcessing(Guid.Empty, Now, Now));
    }

    [Fact]
    public void ReclaimExpiredLease_CountsAnAttempt_AndTakesAFreshLease()
    {
        var import = Transferring();

        var failed = import.ReclaimExpiredLease(Now.AddHours(6), maxAttempts: 3, _clock);

        Assert.False(failed);
        Assert.Equal(RecordingImportStatus.Transferring, import.STATUS);
        Assert.Equal(1, import.ATTEMPTS);
        Assert.Equal(Now.AddHours(6), import.LEASE_UNTIL_UTC);
        Assert.Equal("transfer_timeout", import.ERROR_CODE);
    }

    [Fact]
    public void ReclaimExpiredLease_OnTheLastAttempt_Fails()
    {
        var import = Transferring();
        import.ReclaimExpiredLease(Now.AddHours(6), maxAttempts: 2, _clock);

        var failed = import.ReclaimExpiredLease(Now.AddHours(6), maxAttempts: 2, _clock);

        Assert.True(failed);
        Assert.Equal(RecordingImportStatus.Failed, import.STATUS);
        Assert.Equal("transfer_timeout", import.ERROR_CODE);
        Assert.Null(import.LEASE_UNTIL_UTC);
        Assert.Equal(Now, import.COMPLETED_AT_UTC);
    }

    // ---- Processing ----------------------------------------------------------------------------------

    [Fact]
    public void ScheduleNextProcessingCheck_OnlyWhileProcessing()
    {
        var import = Processing();
        import.ScheduleNextProcessingCheck(Now.AddMinutes(9));
        Assert.Equal(Now.AddMinutes(9), import.NEXT_ATTEMPT_AT_UTC);

        Assert.Throws<InvalidOperationException>(() => Waiting().ScheduleNextProcessingCheck(Now));
    }

    [Fact]
    public void MarkAttached_IsTerminal_RecordsTheLesson_AndClearsTheDeadlines()
    {
        var import = Processing();
        var episode = Guid.NewGuid();

        import.MarkAttached(episode, _clock);

        Assert.Equal(RecordingImportStatus.Attached, import.STATUS);
        Assert.Equal(episode, import.EPISODE_ID);
        Assert.Equal(Now, import.COMPLETED_AT_UTC);
        Assert.Null(import.NEXT_ATTEMPT_AT_UTC);
        Assert.Null(import.LEASE_UNTIL_UTC);
        Assert.Null(import.ERROR_CODE);
        Assert.True(import.IsTerminal);
        Assert.False(import.CanRetry); // nothing to retry: it worked
    }

    [Fact]
    public void MarkAttached_OnlyFromProcessing_AndWithARealEpisode()
    {
        Assert.Throws<InvalidOperationException>(() => Waiting().MarkAttached(Guid.NewGuid(), _clock));
        Assert.Throws<InvalidOperationException>(() => Transferring().MarkAttached(Guid.NewGuid(), _clock));
        Assert.Throws<ArgumentException>(() => Processing().MarkAttached(Guid.Empty, _clock));
    }

    // ---- Transient failures --------------------------------------------------------------------------

    [Fact]
    public void RecordTransientFailure_CountsAttempts_AndSchedulesTheBackoff_UntilTheMaximum()
    {
        var import = Waiting();

        Assert.False(import.RecordTransientFailure("google_transient", TimeSpan.FromMinutes(10), maxAttempts: 3, _clock));
        Assert.Equal(RecordingImportStatus.Waiting, import.STATUS);
        Assert.Equal(1, import.ATTEMPTS);
        Assert.Equal(Now.AddMinutes(10), import.NEXT_ATTEMPT_AT_UTC);
        Assert.Equal("google_transient", import.ERROR_CODE);

        Assert.False(import.RecordTransientFailure("google_transient", TimeSpan.FromMinutes(20), maxAttempts: 3, _clock));
        Assert.Equal(Now.AddMinutes(20), import.NEXT_ATTEMPT_AT_UTC);

        Assert.True(import.RecordTransientFailure("google_transient", TimeSpan.FromMinutes(40), maxAttempts: 3, _clock));
        Assert.Equal(RecordingImportStatus.Failed, import.STATUS);
        Assert.Equal(3, import.ATTEMPTS);
        Assert.Null(import.NEXT_ATTEMPT_AT_UTC);
        Assert.Equal(Now, import.COMPLETED_AT_UTC);
        Assert.True(import.CanRetry);
    }

    [Fact]
    public void RecordTransientFailure_ATransferGoesBackToWaiting_AndReleasesItsLease()
    {
        var import = Transferring();

        import.RecordTransientFailure("ingest_failed", TimeSpan.FromMinutes(10), maxAttempts: 6, _clock);

        Assert.Equal(RecordingImportStatus.Waiting, import.STATUS);
        Assert.Null(import.LEASE_UNTIL_UTC);
        Assert.Equal(Now.AddMinutes(10), import.NEXT_ATTEMPT_AT_UTC);
    }

    [Fact]
    public void RecordTransientFailure_AProcessingImportStaysProcessing_AndKeepsItsTranscodeDeadline()
    {
        var import = Processing();

        import.RecordTransientFailure("attach_failed", TimeSpan.FromMinutes(10), maxAttempts: 6, _clock);

        Assert.Equal(RecordingImportStatus.Processing, import.STATUS);
        Assert.Equal(Now.AddHours(6), import.LEASE_UNTIL_UTC);
        Assert.Equal(Now.AddMinutes(10), import.NEXT_ATTEMPT_AT_UTC);
    }

    [Fact]
    public void RecordTransientFailure_NeverOnATerminalImport()
    {
        var import = Waiting();
        import.MarkSkipped("recording_exists", _clock);

        Assert.Throws<InvalidOperationException>(() => import.RecordTransientFailure("x", TimeSpan.Zero, 6, _clock));
    }

    [Fact]
    public void ErrorCodes_AreCappedAtTheColumnWidth()
    {
        var import = Waiting();

        import.MarkFailed(new string('x', 200), _clock);

        Assert.Equal(SESSION_RECORDING_IMPORT.ErrorCodeMaxLength, import.ERROR_CODE!.Length);
    }

    // ---- Terminal exits ------------------------------------------------------------------------------

    [Theory]
    [InlineData(RecordingImportStatus.Failed, true)]
    [InlineData(RecordingImportStatus.NoRecording, true)]
    [InlineData(RecordingImportStatus.NeedsReconnect, true)]
    [InlineData(RecordingImportStatus.Skipped, false)]
    public void TerminalExits_RecordTheCode_ClearTheSchedule_AndSayWhetherARetryIsAllowed(RecordingImportStatus terminal, bool retryable)
    {
        var import = Transferring();

        switch (terminal)
        {
            case RecordingImportStatus.Failed:
                import.MarkFailed("file_too_large", _clock);
                break;
            case RecordingImportStatus.NoRecording:
                import.MarkNoRecording("no_recording_found", _clock);
                break;
            case RecordingImportStatus.NeedsReconnect:
                import.MarkNeedsReconnect("recording_scope_missing", _clock);
                break;
            default:
                import.MarkSkipped("recording_exists", _clock);
                break;
        }

        Assert.Equal(terminal, import.STATUS);
        Assert.NotNull(import.ERROR_CODE);
        Assert.Equal(Now, import.COMPLETED_AT_UTC);
        Assert.Null(import.NEXT_ATTEMPT_AT_UTC);
        Assert.Null(import.LEASE_UNTIL_UTC);
        Assert.True(import.IsTerminal);
        Assert.Equal(retryable, import.CanRetry);
    }

    [Fact]
    public void TerminalExits_AreNotRepeatable()
    {
        var import = Waiting();
        import.MarkNoRecording("no_recording_found", _clock);

        Assert.Throws<InvalidOperationException>(() => import.MarkFailed("x", _clock));
        Assert.Throws<InvalidOperationException>(() => import.MarkSkipped("x", _clock));
        Assert.Throws<ArgumentException>(() => Waiting().MarkFailed(" ", _clock));
    }

    // ---- Retry ---------------------------------------------------------------------------------------

    [Theory]
    [InlineData(RecordingImportStatus.Failed)]
    [InlineData(RecordingImportStatus.NoRecording)]
    [InlineData(RecordingImportStatus.NeedsReconnect)]
    public void ResetForRetry_BackToWaiting_DueNow_WithEverythingForgotten(RecordingImportStatus from)
    {
        var import = Processing();
        switch (from)
        {
            case RecordingImportStatus.Failed:
                import.MarkFailed("transcode_failed", _clock);
                break;
            case RecordingImportStatus.NoRecording:
                import.MarkNoRecording("no_recording_found", _clock);
                break;
            default:
                import.MarkNeedsReconnect("invalid_grant", _clock);
                break;
        }

        _clock.UtcNow = Now.AddHours(5);
        import.ResetForRetry(_clock);

        Assert.Equal(RecordingImportStatus.Waiting, import.STATUS);
        Assert.Equal(0, import.ATTEMPTS);
        Assert.Equal(Now.AddHours(5), import.NEXT_ATTEMPT_AT_UTC);
        Assert.Null(import.LEASE_UNTIL_UTC);
        Assert.Null(import.GOOGLE_RECORDING_NAME);
        Assert.Null(import.GOOGLE_FILE_ID);
        Assert.Null(import.MEDIA_ASSET_ID); // the failed copy is not reused
        Assert.Null(import.EPISODE_ID);
        Assert.Null(import.ERROR_CODE);
        Assert.Null(import.COMPLETED_AT_UTC);
        Assert.False(import.IsTerminal);
    }

    [Fact]
    public void ResetForRetry_NotWhileWorkIsInProgress_NorAfterSuccess_NorWhenSkipped()
    {
        Assert.Throws<InvalidOperationException>(() => Waiting().ResetForRetry(_clock));
        Assert.Throws<InvalidOperationException>(() => Transferring().ResetForRetry(_clock));
        Assert.Throws<InvalidOperationException>(() => Processing().ResetForRetry(_clock));

        var attached = Processing();
        attached.MarkAttached(Guid.NewGuid(), _clock);
        Assert.Throws<InvalidOperationException>(() => attached.ResetForRetry(_clock));

        var skipped = Waiting();
        skipped.MarkSkipped("recording_exists", _clock);
        Assert.Throws<InvalidOperationException>(() => skipped.ResetForRetry(_clock));
    }
}
