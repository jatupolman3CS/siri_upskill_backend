using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;

namespace Siri.UnitTests.Live;

/// <summary>The instructor side of the recording import (docs/contracts/P11-13-live-recording-auto-import.md sections 6-7): the <c>recordingImport</c> DTO of a session, the
/// <c>canRetry</c> rule, and the retry itself — ownership (404/403), the allowed states, the 30-day window, and a lost race with the job.</summary>
public class RecordingImportServiceTests
{
    private static readonly DateTime Now = LiveTestData.Now;
    private static readonly Guid InstructorId = RecordingJobHarness.InstructorId;

    private readonly RecordingJobHarness _h = new();

    private RecordingImportService Service() => _h.Service();

    private static IReadOnlyDictionary<Guid, SESSION_MEETING> Rooms(RecordingJobHarness h) => h.Meetings.Meetings.ToDictionary(m => m.SESSION_ID);

    private SESSION_RECORDING_IMPORT InState(LiveSessionContext context, RecordingImportStatus status)
    {
        var import = _h.Import(context);
        switch (status)
        {
            case RecordingImportStatus.Waiting:
                break;
            case RecordingImportStatus.Transferring:
                import.BeginTransfer("n", "f", Now.AddHours(1));
                break;
            case RecordingImportStatus.Processing:
                import.BeginTransfer("n", "f", Now.AddHours(1));
                import.MarkProcessing(Guid.NewGuid(), Now.AddMinutes(2), Now.AddHours(6));
                break;
            case RecordingImportStatus.Attached:
                import.BeginTransfer("n", "f", Now.AddHours(1));
                import.MarkProcessing(Guid.NewGuid(), Now, Now.AddHours(6));
                import.MarkAttached(Guid.NewGuid(), _h.Clock);
                break;
            case RecordingImportStatus.NoRecording:
                import.MarkNoRecording("no_recording_found", _h.Clock);
                break;
            case RecordingImportStatus.Failed:
                import.MarkFailed("file_too_large", _h.Clock);
                break;
            case RecordingImportStatus.NeedsReconnect:
                import.MarkNeedsReconnect("recording_scope_missing", _h.Clock);
                break;
            case RecordingImportStatus.Skipped:
                import.MarkSkipped("recording_exists", _h.Clock);
                break;
        }

        return import;
    }

    // ---- The DTO -------------------------------------------------------------------------------------

    [Fact]
    public async Task Info_NoRowAndAManualInstructor_IsManual_WithNothingToRetry()
    {
        _h.Account(hostedDomain: null);
        var context = _h.Session();

        var info = (await Service().GetInfosAsync(InstructorId, [context], Rooms(_h), CancellationToken.None))[context.SessionId];

        Assert.Equal(new RecordingImportInfo(RecordingImportMode.Manual, null, null, null, false), info);
    }

    [Fact]
    public async Task Info_NoRowButAnAutoInstructor_IsAuto_AndTheEndedClassCanBeRetried()
    {
        _h.Account();
        var context = _h.Session();

        var info = await Service().GetInfoAsync(InstructorId, context, _h.Meetings.Meetings[0], CancellationToken.None);

        Assert.Equal(RecordingImportMode.Auto, info.Mode);
        Assert.Null(info.Status);
        Assert.True(info.CanRetry);
    }

    [Fact]
    public async Task Info_AWaitingRow_ShowsTheStatusAndWhenItWillLookAgain_ButNotTheInternals()
    {
        _h.Account();
        var context = _h.Session();
        var import = InState(context, RecordingImportStatus.Waiting);
        import.ScheduleNextSearch(Now.AddMinutes(20));

        var info = (await Service().GetInfosAsync(InstructorId, [context], Rooms(_h), CancellationToken.None))[context.SessionId];

        Assert.Equal(new RecordingImportInfo(RecordingImportMode.Auto, RecordingImportStatus.Waiting, null, Now.AddMinutes(20), false), info);
    }

    [Theory]
    [InlineData(RecordingImportStatus.Waiting, true, false)]
    [InlineData(RecordingImportStatus.Transferring, false, false)]
    [InlineData(RecordingImportStatus.Processing, true, false)]
    [InlineData(RecordingImportStatus.Attached, false, false)]
    [InlineData(RecordingImportStatus.NoRecording, false, true)]
    [InlineData(RecordingImportStatus.Failed, false, true)]
    [InlineData(RecordingImportStatus.NeedsReconnect, false, true)]
    [InlineData(RecordingImportStatus.Skipped, false, false)]
    public async Task Info_PerStatus_NextAttemptOnlyWhileWaitingOrProcessing_AndRetryOnlyFromTheThreeRetryableOnes(
        RecordingImportStatus status, bool hasNextAttempt, bool canRetry)
    {
        _h.Account();
        var context = _h.Session();
        InState(context, status);

        var info = (await Service().GetInfosAsync(InstructorId, [context], Rooms(_h), CancellationToken.None))[context.SessionId];

        Assert.Equal(RecordingImportMode.Auto, info.Mode);
        Assert.Equal(status, info.Status);
        Assert.Equal(hasNextAttempt, info.NextAttemptAtUtc is not null);
        Assert.Equal(canRetry, info.CanRetry);
    }

    [Fact]
    public async Task Info_ErrorCodeIsReported_ExceptOnSuccess()
    {
        _h.Account();
        var failed = _h.Session();
        var attached = _h.Session();
        InState(failed, RecordingImportStatus.Failed);
        InState(attached, RecordingImportStatus.Attached);

        var infos = await Service().GetInfosAsync(InstructorId, [failed, attached], Rooms(_h), CancellationToken.None);

        Assert.Equal("file_too_large", infos[failed.SessionId].ErrorCode);
        Assert.Null(infos[attached.SessionId].ErrorCode);
    }

    [Fact]
    public async Task Info_WithTheFeatureSwitchedOff_IsManualForEverySession_EvenWhereAnOldRowExists_ButTheRowStillShowsAndNothingCanBeRetried()
    {
        // Contract section 2: Enabled=false means the mode is Manual for every session. The old row is history: its status and code still show, canRetry is false.
        _h.Account();
        var failed = _h.Session();
        var attached = _h.Session();
        var untouched = _h.Session();
        InState(failed, RecordingImportStatus.Failed);
        InState(attached, RecordingImportStatus.Attached);
        _h.Enabled = false;

        var infos = await Service().GetInfosAsync(InstructorId, [failed, attached, untouched], Rooms(_h), CancellationToken.None);

        Assert.All(infos.Values, info => Assert.Equal(RecordingImportMode.Manual, info.Mode));
        Assert.All(infos.Values, info => Assert.False(info.CanRetry));
        Assert.Equal(RecordingImportStatus.Failed, infos[failed.SessionId].Status);
        Assert.Equal("file_too_large", infos[failed.SessionId].ErrorCode);
        Assert.Equal(RecordingImportStatus.Attached, infos[attached.SessionId].Status);
        Assert.Null(infos[untouched.SessionId].Status);
    }

    [Fact]
    public void ToInfo_ModeIsAutoOnlyWhileTheFeatureIsOn_AndARowOrAnAutoCapabilityExists()
    {
        var context = Ended();
        var row = SESSION_RECORDING_IMPORT.Create(context.SessionId, context.CourseId, InstructorId, Now, Now);

        Assert.Equal(RecordingImportMode.Auto, RecordingImportService.ToInfo(row, RecordingCapability.Manual, context, true, autoImportEnabled: true, Now).Mode);
        Assert.Equal(RecordingImportMode.Auto, RecordingImportService.ToInfo(null, RecordingCapability.Auto, context, true, autoImportEnabled: true, Now).Mode);
        Assert.Equal(RecordingImportMode.Manual, RecordingImportService.ToInfo(null, RecordingCapability.Manual, context, true, autoImportEnabled: true, Now).Mode);
        Assert.Equal(RecordingImportMode.Manual, RecordingImportService.ToInfo(row, RecordingCapability.Auto, context, true, autoImportEnabled: false, Now).Mode);
        Assert.Equal(RecordingImportMode.Manual, RecordingImportService.ToInfo(null, RecordingCapability.Auto, context, true, autoImportEnabled: false, Now).Mode);
    }

    [Fact]
    public async Task Info_IsComputedForAWholePageInOneRead()
    {
        _h.Account();
        var sessions = Enumerable.Range(0, 5).Select(_ => _h.Session()).ToList();
        InState(sessions[0], RecordingImportStatus.Failed);

        var infos = await Service().GetInfosAsync(InstructorId, sessions, Rooms(_h), CancellationToken.None);

        Assert.Equal(5, infos.Count);
        Assert.Equal(RecordingImportStatus.Failed, infos[sessions[0].SessionId].Status);
        Assert.All(sessions.Skip(1), s => Assert.Null(infos[s.SessionId].Status));
    }

    [Fact]
    public async Task Info_EmptyPage_ReadsNothing()
    {
        var infos = await Service().GetInfosAsync(InstructorId, [], new Dictionary<Guid, SESSION_MEETING>(), CancellationToken.None);

        Assert.Empty(infos);
    }

    // ---- CanRetry (pure) ------------------------------------------------------------------------------

    private LiveSessionContext Ended(TimeSpan? endedAgo = null, LiveSessionStatus status = LiveSessionStatus.Scheduled, Guid? recording = null) =>
        LiveTestData.Context(instructorUserId: InstructorId, status: status, startsAtUtc: Now - (endedAgo ?? TimeSpan.FromHours(1)) - TimeSpan.FromHours(1), endsAtUtc: Now - (endedAgo ?? TimeSpan.FromHours(1)))
        with { RecordingEpisodeId = recording };

    [Fact]
    public void CanRetry_RowRules()
    {
        var failed = SESSION_RECORDING_IMPORT.Create(Guid.NewGuid(), Guid.NewGuid(), InstructorId, Now, Now);
        failed.MarkFailed("x", _h.Clock);

        Assert.True(RecordingImportService.CanRetry(failed, RecordingCapability.Manual, Ended(), hasGoogleRoom: false, autoImportEnabled: true, Now)); // a row needs neither capability nor a room
        Assert.False(RecordingImportService.CanRetry(failed, RecordingCapability.Auto, Ended(), true, autoImportEnabled: false, Now));
        Assert.False(RecordingImportService.CanRetry(failed, RecordingCapability.Auto, Ended(status: LiveSessionStatus.Cancelled), true, true, Now));
        Assert.False(RecordingImportService.CanRetry(failed, RecordingCapability.Auto, Ended(recording: Guid.NewGuid()), true, true, Now));
    }

    [Fact]
    public void CanRetry_TheClassMustBeOver_AndNotOlderThanThirtyDays()
    {
        var failed = SESSION_RECORDING_IMPORT.Create(Guid.NewGuid(), Guid.NewGuid(), InstructorId, Now, Now);
        failed.MarkFailed("x", _h.Clock);

        Assert.False(RecordingImportService.CanRetry(failed, RecordingCapability.Auto, Ended(endedAgo: TimeSpan.FromMinutes(-10)), true, true, Now)); // still running
        Assert.True(RecordingImportService.CanRetry(failed, RecordingCapability.Auto, Ended(endedAgo: TimeSpan.FromDays(29)), true, true, Now));
        Assert.True(RecordingImportService.CanRetry(failed, RecordingCapability.Auto, Ended(endedAgo: RecordingImportService.RetryWindow), true, true, Now)); // exactly at the edge
        Assert.False(RecordingImportService.CanRetry(failed, RecordingCapability.Auto, Ended(endedAgo: RecordingImportService.RetryWindow + TimeSpan.FromMinutes(1)), true, true, Now));
    }

    [Theory]
    [InlineData(RecordingCapability.Auto, true, true)]
    [InlineData(RecordingCapability.Auto, false, false)]
    [InlineData(RecordingCapability.AutoNeedsConsent, true, false)]
    [InlineData(RecordingCapability.Manual, true, false)]
    public void CanRetry_NoRow_NeedsAutoCapability_AndAGoogleRoom(RecordingCapability capability, bool hasGoogleRoom, bool expected)
    {
        Assert.Equal(expected, RecordingImportService.CanRetry(null, capability, Ended(), hasGoogleRoom, true, Now));
    }

    // ---- Retry ---------------------------------------------------------------------------------------

    [Fact]
    public async Task Retry_UnknownSession_Is404()
    {
        _h.Account();

        var result = await Service().RetryAsync(InstructorId, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task Retry_SomeoneElsesSession_Is403_AndNothingChanges()
    {
        _h.Account();
        var context = _h.Session();
        var import = InState(context, RecordingImportStatus.Failed);

        var result = await Service().RetryAsync(Guid.NewGuid(), context.SessionId, CancellationToken.None);

        Assert.Equal("forbidden", result.Error.Code);
        Assert.Equal(RecordingImportStatus.Failed, import.STATUS);
        Assert.Equal(0, _h.Imports.SaveCount);
    }

    [Theory]
    [InlineData(RecordingImportStatus.Failed)]
    [InlineData(RecordingImportStatus.NoRecording)]
    [InlineData(RecordingImportStatus.NeedsReconnect)]
    public async Task Retry_ARetryableImport_GoesBackToWaiting_DueNow(RecordingImportStatus status)
    {
        _h.Account();
        var context = _h.Session();
        var import = InState(context, status);

        var result = await Service().RetryAsync(InstructorId, context.SessionId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RecordingImportStatus.Waiting, import.STATUS);
        Assert.Equal(0, import.ATTEMPTS);
        Assert.Equal(Now, import.NEXT_ATTEMPT_AT_UTC);
        Assert.Equal(1, _h.Imports.SaveCount);
        Assert.Equal(new RecordingImportInfo(RecordingImportMode.Auto, RecordingImportStatus.Waiting, null, Now, CanRetry: false), result.Value);
    }

    [Theory]
    [InlineData(RecordingImportStatus.Waiting)]
    [InlineData(RecordingImportStatus.Transferring)]
    [InlineData(RecordingImportStatus.Processing)]
    [InlineData(RecordingImportStatus.Attached)]
    [InlineData(RecordingImportStatus.Skipped)]
    public async Task Retry_AnImportThatIsRunningDoneOrSkipped_Is409(RecordingImportStatus status)
    {
        _h.Account();
        var context = _h.Session();
        var import = InState(context, status);

        var result = await Service().RetryAsync(InstructorId, context.SessionId, CancellationToken.None);

        AssertNotRetryable(result);
        Assert.Equal(status, import.STATUS);
        Assert.Equal(0, _h.Imports.SaveCount);
    }

    [Fact]
    public async Task Retry_NoImportYet_ForAnAutoInstructorWithAGoogleRoom_CreatesAWaitingOneDueNow()
    {
        _h.Account();
        var context = _h.Session();

        var result = await Service().RetryAsync(InstructorId, context.SessionId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(_h.Imports.Imports);
        Assert.Equal(context.SessionId, row.SESSION_ID);
        Assert.Equal(context.CourseId, row.COURSE_ID);
        Assert.Equal(InstructorId, row.INSTRUCTOR_USER_ID);
        Assert.Equal(RecordingImportStatus.Waiting, row.STATUS);
        Assert.Equal(Now, row.NEXT_ATTEMPT_AT_UTC);
        Assert.Equal(context.EndsAtUtc.AddHours(12), row.SEARCH_UNTIL_UTC);
        Assert.Equal(RecordingImportStatus.Waiting, result.Value.Status);
    }

    [Fact]
    public async Task Retry_NoImportYet_ForAPersonalOrUnconsentedInstructor_Is409()
    {
        _h.Account(hostedDomain: null);
        var context = _h.Session();
        AssertNotRetryable(await Service().RetryAsync(InstructorId, context.SessionId, CancellationToken.None));

        _h.Accounts.Accounts.Clear();
        _h.Account(scopes: GoogleScopesJustCalendar);
        AssertNotRetryable(await Service().RetryAsync(InstructorId, context.SessionId, CancellationToken.None));

        Assert.Empty(_h.Imports.Imports);
    }

    private const string GoogleScopesJustCalendar = Siri.Integrations.Google.GoogleScopes.CalendarEventsOwned;

    [Fact]
    public async Task Retry_FeatureOff_Is409_EvenForAFailedImport()
    {
        _h.Account();
        var context = _h.Session();
        var import = InState(context, RecordingImportStatus.Failed);
        _h.Enabled = false;

        AssertNotRetryable(await Service().RetryAsync(InstructorId, context.SessionId, CancellationToken.None));
        Assert.Equal(RecordingImportStatus.Failed, import.STATUS);
    }

    [Fact]
    public async Task Retry_NoGoogleRoom_Cancelled_AlreadyRecorded_NotOverYet_OrTooOld_AreAll409()
    {
        _h.Account();

        var manualRoom = _h.Session(roomProvider: MeetingProvider.Manual, roomUrl: "https://zoom.us/j/1");
        var noRoom = _h.Session(withRoom: false);
        var cancelled = _h.Session(status: LiveSessionStatus.Cancelled);
        var recorded = _h.Session(recordingEpisodeId: Guid.NewGuid());
        var running = _h.Session(endedAgo: TimeSpan.FromMinutes(-30));
        var tooOld = _h.Session(endedAgo: TimeSpan.FromDays(31));

        foreach (var session in new[] { manualRoom, noRoom, cancelled, recorded, running, tooOld })
        {
            AssertNotRetryable(await Service().RetryAsync(InstructorId, session.SessionId, CancellationToken.None));
        }

        Assert.Empty(_h.Imports.Imports);
    }

    [Fact]
    public async Task Retry_LosingARaceWithTheJob_Is409_NotA500()
    {
        _h.Account();
        var context = _h.Session();
        var import = InState(context, RecordingImportStatus.Failed);
        _h.Imports.ThrowOnNextSave = RecordingJobHarness.Conflict();

        var result = await Service().RetryAsync(InstructorId, context.SessionId, CancellationToken.None);

        AssertNotRetryable(result);
        Assert.Equal(1, _h.Imports.ClearTrackingCount);
    }

    [Fact]
    public async Task Retry_TheUniqueSessionIndexRefusingASecondRow_Is409_NotA500()
    {
        _h.Account();
        var context = _h.Session();
        _h.Imports.ThrowOnNextSave = new DbUpdateException("duplicate key");

        var result = await Service().RetryAsync(InstructorId, context.SessionId, CancellationToken.None);

        AssertNotRetryable(result);
        Assert.Equal(1, _h.Imports.ClearTrackingCount);
    }

    private static void AssertNotRetryable(Siri.SharedKernel.Result<RecordingImportInfo> result)
    {
        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(LiveReasons.RecordingImportNotRetryable, result.Error.Reason);
    }

    // ---- Wiring into the instructor's session list / detail -----------------------------------------------

    [Fact]
    public async Task SessionList_CarriesTheRecordingImportOfEachSession()
    {
        var h = new JoinHarness();
        h.WithOptions(o => o.Recording.AutoImport.Enabled = true);
        var instructor = Guid.NewGuid();
        var endedSession = h.AddSession(
            instructor, startsAtUtc: LiveTestData.Now.AddHours(-3), endsAtUtc: LiveTestData.Now.AddHours(-1));
        var room = SESSION_MEETING.Stage(endedSession.SessionId);
        room.RecordGoogleSynced(MeetingProvider.GoogleMeet, "evt", h.Protector.Encrypt(JoinHarness.RoomUrl), Guid.NewGuid(), h.Clock);
        h.Meetings.Meetings.Add(room);
        var account = INSTRUCTOR_GOOGLE_ACCOUNT.Connect(
            instructor, "sub", "t@school.example.test", "enc", $"{Siri.Integrations.Google.GoogleScopes.CalendarEventsOwned} {string.Join(' ', Siri.Integrations.Google.GoogleScopes.RecordingScopes)}", h.Clock, "school.example.test");
        h.Accounts.Accounts.Add(account);

        var page = await h.InstructorQueries().GetSessionsAsync(instructor, InstructorSessionScope.Past, null, 1, 20, CancellationToken.None);

        var item = Assert.Single(page.Items);
        Assert.Equal(RecordingImportMode.Auto, item.RecordingImport.Mode);
        Assert.Null(item.RecordingImport.Status);
        Assert.True(item.RecordingImport.CanRetry);
    }

    [Fact]
    public async Task SessionDetail_CarriesTheRecordingImport_AndNeverAnInternalId()
    {
        var h = new JoinHarness();
        h.WithOptions(o => o.Recording.AutoImport.Enabled = true);
        var instructor = Guid.NewGuid();
        var session = h.AddSession(instructor, startsAtUtc: LiveTestData.Now.AddHours(-3), endsAtUtc: LiveTestData.Now.AddHours(-1));
        var import = SESSION_RECORDING_IMPORT.Create(session.SessionId, session.CourseId, instructor, LiveTestData.Now, LiveTestData.Now.AddHours(12));
        import.BeginTransfer("conferenceRecords/SECRET-REC", "SECRET-DRIVE-FILE", LiveTestData.Now.AddHours(3));
        import.MarkProcessing(Guid.NewGuid(), LiveTestData.Now.AddMinutes(2), LiveTestData.Now.AddHours(6));
        h.RecordingImports.Imports.Add(import);

        var result = await h.InstructorQueries().GetSessionDetailAsync(instructor, session.SessionId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(RecordingImportStatus.Processing, result.Value.RecordingImport.Status);
        var json = System.Text.Json.JsonSerializer.Serialize(result.Value.RecordingImport);
        Assert.DoesNotContain("SECRET", json);
        Assert.DoesNotContain(import.MEDIA_ASSET_ID!.Value.ToString(), json);
    }
}
