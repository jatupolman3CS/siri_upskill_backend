using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.HttpResults;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.SharedKernel;

namespace Siri.UnitTests.Live;

/// <summary>
/// The join gate (docs/contracts/P11-05-live-learner-instructor-api-join-gate.md section 4.1): the exact order of the checks, a byte-identical 404 for "no such
/// session"/"not entitled"/"enrollment over" (no existence oracle), the window edges to the tick, the owner's early entry, "no log, no link", and that the room link
/// appears in nothing but the successful response.
/// </summary>
public class SessionJoinServiceTests
{
    private static readonly Guid LearnerId = Guid.NewGuid();
    private static readonly Guid InstructorId = Guid.NewGuid();
    private static readonly Guid AuthSessionId = Guid.NewGuid();

    private const string Ip = "203.0.113.7";
    private const string Agent = "Mozilla/5.0 (test)";

    private readonly JoinHarness _h = new();

    private Task<Result<JoinLiveSessionResponse>> Join(Guid userId, Guid sessionId, Guid? sid = null) =>
        _h.JoinService().JoinAsync(userId, sessionId, sid ?? AuthSessionId, Ip, Agent, CancellationToken.None);

    /// <summary>A session the learner is enrolled in with a ready room, starting at <paramref name="startsAt"/> (default: 5 minutes from now = inside the window).</summary>
    private LiveSessionContext ReadySession(
        DateTime? startsAt = null,
        DateTime? endsAt = null,
        LiveSessionStatus status = LiveSessionStatus.Scheduled,
        bool enrolled = true,
        bool withMeeting = true,
        Guid? recordingEpisodeId = null)
    {
        var context = _h.AddSession(InstructorId, status: status, startsAtUtc: startsAt ?? LiveTestData.Now.AddMinutes(5), endsAtUtc: endsAt, recordingEpisodeId: recordingEpisodeId);
        if (enrolled)
        {
            _h.Enroll(LearnerId, context.CourseId);
        }

        if (withMeeting)
        {
            _h.AddMeeting(context.SessionId);
        }

        return context;
    }

    private static string Render(DomainError error)
    {
        var problem = Assert.IsType<ProblemHttpResult>(error.ToProblemHttpResult(new DefaultHttpContext()));
        var details = problem.ProblemDetails;
        details.Extensions.Remove("traceId"); // the only member that is allowed to differ

        return JsonSerializer.Serialize(new
        {
            problem.StatusCode,
            details.Type,
            details.Title,
            details.Detail,
            details.Instance,
            Extensions = details.Extensions.OrderBy(e => e.Key, StringComparer.Ordinal).Select(e => new { e.Key, e.Value }).ToArray(),
        });
    }

    private void AssertNoLinkAnywhereButTheResponse()
    {
        var logs = string.Join('\n', _h.JoinLogger.Messages.Concat(_h.MeetingLogger.Messages));
        Assert.DoesNotContain("meet.google.com", logs, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET-ROOM-TOKEN", logs, StringComparison.OrdinalIgnoreCase);

        foreach (var row in _h.JoinLogs.Committed.Concat(_h.JoinLogs.Staged))
        {
            Assert.DoesNotContain("meet.google.com", row.IP_ADDRESS ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("meet.google.com", row.USER_AGENT ?? string.Empty, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---- The happy path -----------------------------------------------------------------------------

    [Fact]
    public async Task Join_EnrolledLearnerInsideTheWindow_ReturnsTheLinkAndTheDecryptedUrlIsTheStoredOne()
    {
        var context = ReadySession();

        var result = await Join(LearnerId, context.SessionId);

        Assert.True(result.IsSuccess);
        Assert.Equal(context.SessionId, result.Value.SessionId);
        Assert.Equal(JoinHarness.RoomUrl, result.Value.MeetUrl);
        Assert.Equal(context.StartsAtUtc, result.Value.StartsAtUtc);
        Assert.Equal(context.EndsAtUtc, result.Value.EndsAtUtc);
        Assert.Equal(LiveTestData.Now, result.Value.ServerTimeUtc);
    }

    [Fact]
    public async Task Join_Success_CommitsOneLearnerLogRowWithTheForensicFields()
    {
        var context = ReadySession();

        await Join(LearnerId, context.SessionId);

        var row = Assert.Single(_h.JoinLogs.Committed);
        Assert.Empty(_h.JoinLogs.Staged);
        Assert.Equal(context.SessionId, row.SESSION_ID);
        Assert.Equal(context.CourseId, row.COURSE_ID);
        Assert.Equal(LearnerId, row.USER_ID);
        Assert.Equal(LiveParticipantRole.Learner, row.ROLE);
        Assert.Equal(AuthSessionId, row.AUTH_SESSION_ID);
        Assert.Equal(LiveTestData.Now, row.JOINED_AT_UTC);
        Assert.Equal(Ip, row.IP_ADDRESS);
        Assert.Equal(Agent, row.USER_AGENT);
    }

    [Fact]
    public async Task Join_AMissingOrBlankAuthSessionAndAddress_StillWorks_WithNullsInTheLog()
    {
        var context = ReadySession();

        var result = await _h.JoinService().JoinAsync(LearnerId, context.SessionId, authSessionId: null, ipAddress: null, userAgent: "  ", CancellationToken.None);

        Assert.True(result.IsSuccess);
        var row = Assert.Single(_h.JoinLogs.Committed);
        Assert.Null(row.AUTH_SESSION_ID);
        Assert.Null(row.IP_ADDRESS);
        Assert.Null(row.USER_AGENT);
    }

    [Fact]
    public async Task Join_AVeryLongUserAgent_IsCutAt300Characters()
    {
        var context = ReadySession();

        await _h.JoinService().JoinAsync(LearnerId, context.SessionId, AuthSessionId, Ip, new string('x', 500), CancellationToken.None);

        Assert.Equal(300, Assert.Single(_h.JoinLogs.Committed).USER_AGENT!.Length);
    }

    [Fact]
    public async Task Join_CalledTwice_AppendsASecondRow_NeverUpdatesTheFirst()
    {
        var context = ReadySession();

        await Join(LearnerId, context.SessionId);
        _h.Clock.UtcNow = LiveTestData.Now.AddSeconds(30);
        await Join(LearnerId, context.SessionId);

        Assert.Equal(2, _h.JoinLogs.Committed.Count);
        Assert.Equal(2, _h.JoinLogs.SaveCount);
        Assert.Equal(LiveTestData.Now, _h.JoinLogs.Committed[0].JOINED_AT_UTC);
        Assert.Equal(LiveTestData.Now.AddSeconds(30), _h.JoinLogs.Committed[1].JOINED_AT_UTC);
    }

    // ---- "No log, no link" ---------------------------------------------------------------------------

    [Fact]
    public async Task Join_WhenTheLogCannotBeCommitted_NoLinkIsReturned_TheExceptionPropagates()
    {
        var context = ReadySession();
        _h.JoinLogs.ThrowOnSave = new InvalidOperationException("database is down");

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() => Join(LearnerId, context.SessionId));

        // The failure surfaces as itself — it is never turned into a success that carries the link, and it never contains it.
        Assert.DoesNotContain("meet.google.com", exception.ToString(), StringComparison.OrdinalIgnoreCase);
        Assert.Empty(_h.JoinLogs.Committed);
        AssertNoLinkAnywhereButTheResponse();
    }

    [Fact]
    public async Task Join_TheLogIsStagedAndSavedBeforeTheResultIsProduced()
    {
        var context = ReadySession();

        var result = await Join(LearnerId, context.SessionId);

        // By the time the caller has the link, the row is already committed (not merely staged).
        Assert.True(result.IsSuccess);
        Assert.Single(_h.JoinLogs.Committed);
        Assert.Empty(_h.JoinLogs.Staged);
        Assert.Equal(1, _h.JoinLogs.SaveCount);
    }

    // ---- 404: no existence oracle ---------------------------------------------------------------------

    [Fact]
    public async Task Join_UnknownSession_IsTheSharedNotFound_AndWritesNoLog()
    {
        var result = await Join(LearnerId, Guid.NewGuid());

        Assert.True(result.IsFailure);
        Assert.Same(LiveErrors.NotFound, result.Error);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Equal(LiveReasons.NotFound, result.Error.Reason);
        Assert.Empty(_h.JoinLogs.Committed);
        Assert.Contains(_h.JoinLogger.Messages, m => m.Contains("live.join.denied") && m.Contains("no_session"));
    }

    [Fact]
    public async Task Join_NotEnrolled_IsTheSharedNotFound_AndWritesNoLog()
    {
        var context = ReadySession(enrolled: false);

        var result = await Join(LearnerId, context.SessionId);

        Assert.Same(LiveErrors.NotFound, result.Error);
        Assert.Empty(_h.JoinLogs.Committed);
        Assert.Contains(_h.JoinLogger.Messages, m => m.Contains("live.join.denied") && m.Contains("not_entitled"));
    }

    [Fact]
    public async Task Join_EnrollmentThatExpiredOrWasRevoked_IsTheSharedNotFound()
    {
        // The Learning contract only reports ACTIVE, unexpired enrollments: a lapsed/revoked one is simply absent, which is the same as never having had one.
        var context = ReadySession(enrolled: false);
        _h.Learning.ActiveEnrollments.Add((Guid.NewGuid(), context.CourseId)); // someone else is enrolled; the caller is not

        var result = await Join(LearnerId, context.SessionId);

        Assert.Same(LiveErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Join_AnotherInstructorOfAnotherCourse_IsTheSharedNotFound()
    {
        var context = ReadySession(enrolled: false);
        var otherInstructor = Guid.NewGuid();
        _h.AddSession(otherInstructor); // they teach something else; that gives them nothing here

        var result = await Join(otherInstructor, context.SessionId);

        Assert.Same(LiveErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Join_AnAdministratorWhoIsNotTheOwner_GetsNoBypass()
    {
        // The service has no notion of roles at all: an administrator is just a user who is neither enrolled nor the owner.
        var context = ReadySession(enrolled: false);
        var administrator = Guid.NewGuid();

        var result = await Join(administrator, context.SessionId);

        Assert.Same(LiveErrors.NotFound, result.Error);
        Assert.Empty(_h.JoinLogs.Committed);
    }

    [Fact]
    public async Task Join_NoSuchSession_NotEntitled_AndLapsedEnrollment_RenderByteIdenticalProblemBodies_ExceptTheTraceId()
    {
        var unknown = await Join(LearnerId, Guid.NewGuid());

        var someoneElses = ReadySession(enrolled: false);
        var notEntitled = await Join(LearnerId, someoneElses.SessionId);

        var lapsed = ReadySession(enrolled: false);
        var lapsedResult = await Join(LearnerId, lapsed.SessionId);

        var a = Render(unknown.Error);
        var b = Render(notEntitled.Error);
        var c = Render(lapsedResult.Error);

        Assert.Equal(a, b);
        Assert.Equal(a, c);
        Assert.Contains("live.not_found", a);
        Assert.Contains("404", a);
    }

    [Fact]
    public async Task Join_EntitlementIsCheckedBeforeEveryStateCheck_SoAStrangerLearnsNothingAboutTheSession()
    {
        // Cancelled, over, not-yet-open and room-less sessions all answer a non-entitled caller with the same 404 — never a 409/503 that would confirm the session exists.
        var cancelled = ReadySession(enrolled: false, status: LiveSessionStatus.Cancelled);
        var ended = ReadySession(enrolled: false, startsAt: LiveTestData.Now.AddHours(-5), endsAt: LiveTestData.Now.AddHours(-3));
        var notYetOpen = ReadySession(enrolled: false, startsAt: LiveTestData.Now.AddDays(3));
        var noRoom = ReadySession(enrolled: false, withMeeting: false);

        foreach (var session in new[] { cancelled, ended, notYetOpen, noRoom })
        {
            var result = await Join(LearnerId, session.SessionId);
            Assert.Same(LiveErrors.NotFound, result.Error);
        }
    }

    // ---- Cancelled / ended / window ----------------------------------------------------------------------

    [Fact]
    public async Task Join_CancelledSession_Is409WithTheCancelledReason_NoLog()
    {
        var context = ReadySession(status: LiveSessionStatus.Cancelled);

        var result = await Join(LearnerId, context.SessionId);

        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(LiveReasons.SessionCancelled, result.Error.Reason);
        Assert.Empty(_h.JoinLogs.Committed);
    }

    [Fact]
    public async Task Join_CancelledWinsOverEndedAndOverANotYetOpenWindow()
    {
        var cancelledAndOver = ReadySession(status: LiveSessionStatus.Cancelled, startsAt: LiveTestData.Now.AddHours(-5), endsAt: LiveTestData.Now.AddHours(-3));
        var cancelledAndFuture = ReadySession(status: LiveSessionStatus.Cancelled, startsAt: LiveTestData.Now.AddDays(5));

        Assert.Equal(LiveReasons.SessionCancelled, (await Join(LearnerId, cancelledAndOver.SessionId)).Error.Reason);
        Assert.Equal(LiveReasons.SessionCancelled, (await Join(LearnerId, cancelledAndFuture.SessionId)).Error.Reason);
    }

    [Fact]
    public async Task Join_EndedSession_Is409WithTheEndedReason_AndPointsToTheRecordingWhenThereIsOne()
    {
        var episodeId = Guid.NewGuid();
        var context = ReadySession(startsAt: LiveTestData.Now.AddHours(-5), endsAt: LiveTestData.Now.AddHours(-3), recordingEpisodeId: episodeId);

        var result = await Join(LearnerId, context.SessionId);

        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(LiveReasons.SessionEnded, result.Error.Reason);
        Assert.Equal("test-course", result.Error.Extensions!["courseSlug"]);
        Assert.Equal(episodeId, result.Error.Extensions["recordingEpisodeId"]);
        Assert.Empty(_h.JoinLogs.Committed);
    }

    [Fact]
    public async Task Join_EndedSessionWithoutARecording_OmitsTheRecordingHintInsteadOfSendingNull()
    {
        var context = ReadySession(startsAt: LiveTestData.Now.AddHours(-5), endsAt: LiveTestData.Now.AddHours(-3));

        var result = await Join(LearnerId, context.SessionId);

        Assert.Equal(LiveReasons.SessionEnded, result.Error.Reason);
        Assert.Equal("test-course", result.Error.Extensions!["courseSlug"]);
        Assert.False(result.Error.Extensions.ContainsKey("recordingEpisodeId"));
    }

    [Fact]
    public async Task Join_WindowNotOpenYet_Is409WithOpensAtAndServerTime()
    {
        var starts = LiveTestData.Now.AddHours(2);
        var context = ReadySession(startsAt: starts);

        var result = await Join(LearnerId, context.SessionId);

        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(LiveReasons.WindowNotOpen, result.Error.Reason);
        Assert.Equal(starts.AddMinutes(-15), result.Error.Extensions!["opensAtUtc"]);
        Assert.Equal(LiveTestData.Now, result.Error.Extensions["serverTimeUtc"]);
        Assert.Empty(_h.JoinLogs.Committed);
    }

    [Fact]
    public async Task Join_AtExactlyTheWindowOpening_Passes()
    {
        var starts = LiveTestData.Now.AddMinutes(15);
        var context = ReadySession(startsAt: starts);

        Assert.True((await Join(LearnerId, context.SessionId)).IsSuccess);
    }

    [Fact]
    public async Task Join_OneTickBeforeTheWindowOpens_IsRefused()
    {
        var starts = LiveTestData.Now.AddMinutes(15).AddTicks(1);
        var context = ReadySession(startsAt: starts);

        var result = await Join(LearnerId, context.SessionId);

        Assert.Equal(LiveReasons.WindowNotOpen, result.Error.Reason);
    }

    [Fact]
    public async Task Join_AtExactlyTheSessionsEnd_Passes_AndOneTickAfterIsRefused()
    {
        var ends = LiveTestData.Now;
        var context = ReadySession(startsAt: ends.AddHours(-2), endsAt: ends);

        Assert.True((await Join(LearnerId, context.SessionId)).IsSuccess);

        _h.Clock.UtcNow = ends.AddTicks(1);
        var late = await Join(LearnerId, context.SessionId);
        Assert.Equal(LiveReasons.SessionEnded, late.Error.Reason);
    }

    [Fact]
    public async Task Join_UsesTheConfiguredWindow_NotAHardcodedFifteenMinutes()
    {
        _h.WithOptions(o => o.JoinWindowBeforeMinutes = 30);
        var context = ReadySession(startsAt: LiveTestData.Now.AddMinutes(25)); // outside 15, inside 30

        Assert.True((await Join(LearnerId, context.SessionId)).IsSuccess);

        var tooEarly = ReadySession(startsAt: LiveTestData.Now.AddMinutes(31));
        var refused = await Join(LearnerId, tooEarly.SessionId);
        Assert.Equal(LiveReasons.WindowNotOpen, refused.Error.Reason);
        Assert.Equal(tooEarly.StartsAtUtc.AddMinutes(-30), refused.Error.Extensions!["opensAtUtc"]);
    }

    [Fact]
    public async Task Join_TimeComesFromTheClock_AMovingClockChangesTheDecision()
    {
        var context = ReadySession(startsAt: LiveTestData.Now.AddHours(1));

        Assert.Equal(LiveReasons.WindowNotOpen, (await Join(LearnerId, context.SessionId)).Error.Reason);

        _h.Clock.UtcNow = LiveTestData.Now.AddMinutes(50); // 10 minutes before the start
        Assert.True((await Join(LearnerId, context.SessionId)).IsSuccess);
    }

    // ---- The owner -------------------------------------------------------------------------------------------

    [Fact]
    public async Task Join_TheOwningInstructor_CanEnterDaysBeforeTheWindow_WithoutAnEnrollment_AndIsLoggedAsInstructor()
    {
        var context = ReadySession(startsAt: LiveTestData.Now.AddDays(4), enrolled: false);

        var result = await Join(InstructorId, context.SessionId);

        Assert.True(result.IsSuccess);
        Assert.Equal(JoinHarness.RoomUrl, result.Value.MeetUrl);
        Assert.Equal(0, _h.Learning.HasActiveEnrollmentCalls); // an owner needs no enrollment
        Assert.Equal(LiveParticipantRole.Instructor, Assert.Single(_h.JoinLogs.Committed).ROLE);
    }

    [Fact]
    public async Task Join_TheOwner_StillCannotEnterACancelledOrFinishedSession()
    {
        var cancelled = ReadySession(status: LiveSessionStatus.Cancelled, enrolled: false);
        var ended = ReadySession(startsAt: LiveTestData.Now.AddHours(-5), endsAt: LiveTestData.Now.AddHours(-3), enrolled: false);

        Assert.Equal(LiveReasons.SessionCancelled, (await Join(InstructorId, cancelled.SessionId)).Error.Reason);
        Assert.Equal(LiveReasons.SessionEnded, (await Join(InstructorId, ended.SessionId)).Error.Reason);
    }

    // ---- The room ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Join_NoMeetingRow_Is503RoomNotReady_NoLog()
    {
        var context = ReadySession(withMeeting: false);

        var result = await Join(LearnerId, context.SessionId);

        Assert.Equal("unavailable", result.Error.Code);
        Assert.Equal(LiveReasons.MeetingNotReady, result.Error.Reason);
        Assert.Empty(_h.JoinLogs.Committed);
    }

    [Fact]
    public async Task Join_MeetingThatHasNoLinkYet_Is503RoomNotReady()
    {
        var context = ReadySession(withMeeting: false);
        _h.Meetings.Add(SESSION_MEETING.Stage(context.SessionId)); // Pending, no URL

        var result = await Join(LearnerId, context.SessionId);

        Assert.Equal(LiveReasons.MeetingNotReady, result.Error.Reason);
        Assert.Empty(_h.JoinLogs.Committed);
    }

    [Fact]
    public async Task Join_DeletedMeeting_Is503RoomNotReady_EvenIfAnOldCiphertextIsStillStored()
    {
        var context = ReadySession();
        var meeting = Assert.Single(_h.Meetings.Meetings);
        meeting.MarkDeleted();

        var result = await Join(LearnerId, context.SessionId);

        Assert.Equal(LiveReasons.MeetingNotReady, result.Error.Reason);
        Assert.Empty(_h.JoinLogs.Committed);
    }

    [Fact]
    public async Task Join_AStoredLinkThatCannotBeDecrypted_IsTheSameRoomNotReady_NotAnError500_AndNothingLeaks()
    {
        var context = ReadySession(withMeeting: false);
        var foreignProtector = LiveTestData.Protector(); // a different key: the ciphertext authenticates against nothing
        var meeting = SESSION_MEETING.Stage(context.SessionId);
        meeting.SetManualLink(foreignProtector.Encrypt(JoinHarness.RoomUrl));
        _h.Meetings.Add(meeting);

        var result = await Join(LearnerId, context.SessionId);

        Assert.Equal("unavailable", result.Error.Code);
        Assert.Equal(LiveReasons.MeetingNotReady, result.Error.Reason);
        Assert.Empty(_h.JoinLogs.Committed);
        AssertNoLinkAnywhereButTheResponse();
        Assert.Contains(_h.MeetingLogger.Messages, m => m.Contains(context.SessionId.ToString()));
    }

    [Theory]
    [InlineData("AAAA")] // valid Base64, far too short to hold a nonce and a tag -> SensitiveDataProtector throws InvalidOperationException
    [InlineData("%%% not base64 %%%")] // not Base64 at all -> FormatException
    public async Task Join_ACorruptedStoredLink_IsRoomNotReady_NeverAnUnhandledException(string corruptCiphertext)
    {
        var context = ReadySession(withMeeting: false);
        var meeting = SESSION_MEETING.Stage(context.SessionId);
        meeting.SetManualLink(corruptCiphertext);
        _h.Meetings.Add(meeting);

        var result = await Join(LearnerId, context.SessionId);

        Assert.Equal(LiveReasons.MeetingNotReady, result.Error.Reason);
        Assert.Empty(_h.JoinLogs.Committed);
        AssertNoLinkAnywhereButTheResponse();
    }

    [Fact]
    public async Task Join_RoomNotReadyAnswer_IsIdenticalWhetherTheRowIsMissingOrUnreadable()
    {
        var missing = ReadySession(withMeeting: false);
        var unreadable = ReadySession(withMeeting: false);
        var broken = SESSION_MEETING.Stage(unreadable.SessionId);
        broken.SetManualLink(LiveTestData.Protector().Encrypt(JoinHarness.RoomUrl));
        _h.Meetings.Add(broken);

        var a = await Join(LearnerId, missing.SessionId);
        var b = await Join(LearnerId, unreadable.SessionId);

        Assert.Equal(Render(a.Error), Render(b.Error));
    }

    // ---- Nothing but the response ever carries the link --------------------------------------------------------

    [Fact]
    public async Task Join_NeverWritesTheLinkToAnyLogLine_OnSuccessOrFailure()
    {
        var ok = ReadySession();
        var cancelled = ReadySession(status: LiveSessionStatus.Cancelled);
        var stranger = ReadySession(enrolled: false);
        var early = ReadySession(startsAt: LiveTestData.Now.AddDays(1));

        await Join(LearnerId, ok.SessionId);
        await Join(LearnerId, cancelled.SessionId);
        await Join(LearnerId, stranger.SessionId);
        await Join(LearnerId, early.SessionId);
        await Join(LearnerId, Guid.NewGuid());

        AssertNoLinkAnywhereButTheResponse();
        Assert.NotEmpty(_h.JoinLogger.Messages); // the denials were logged (just not with the link)
    }

    [Fact]
    public async Task Join_DeniedLogLines_CarryOnlyIdsAndAReason_NeverAnEmailOrAnAddress()
    {
        var stranger = ReadySession(enrolled: false);

        await Join(LearnerId, stranger.SessionId);

        var line = Assert.Single(_h.JoinLogger.Messages, m => m.Contains("live.join.denied"));
        Assert.Contains(LearnerId.ToString(), line);
        Assert.Contains(stranger.SessionId.ToString(), line);
        Assert.DoesNotContain(Ip, line);
        Assert.DoesNotContain("@", line);
    }

    // ---- ResolveEntitledSessionAsync (shared with the calendar download) -------------------------------------------------

    [Fact]
    public async Task ResolveEntitledSession_ForAnEnrolledLearner_ReturnsTheContextWithoutOwnership()
    {
        var context = ReadySession();

        var result = await _h.JoinService().ResolveEntitledSessionAsync(LearnerId, context.SessionId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(result.Value.IsOwner);
        Assert.Equal(context.SessionId, result.Value.Context.SessionId);
    }

    [Fact]
    public async Task ResolveEntitledSession_ForTheOwner_IsOwner()
    {
        var context = ReadySession(enrolled: false);

        var result = await _h.JoinService().ResolveEntitledSessionAsync(InstructorId, context.SessionId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsOwner);
    }
}
