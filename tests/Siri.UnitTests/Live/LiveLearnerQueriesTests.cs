using System.Text.Json;
using System.Text.Json.Serialization;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Contracts;
using Siri.Modules.Live.Domain;
using Siri.SharedKernel;

namespace Siri.UnitTests.Live;

/// <summary>
/// The learner's read side (docs/contracts/P11-05-live-learner-instructor-api-join-gate.md sections 4.2-4.3): my-sessions, upcoming and the calendar file. The tests pin
/// the entitlement answer (the shared 404), that <c>displayState</c>/<c>canJoin</c> are computed on the server clock and can never disagree with the join gate, the
/// batching, the limit bounds, and that none of these responses can ever carry a room link.
/// </summary>
public class LiveLearnerQueriesTests
{
    private static readonly Guid LearnerId = Guid.NewGuid();
    private static readonly Guid InstructorId = Guid.NewGuid();

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() }, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly JoinHarness _h = new();

    private LiveSessionContext Session(Guid courseId, DateTime starts, DateTime? ends = null, LiveSessionStatus status = LiveSessionStatus.Scheduled, string title = "คาบ", Guid? recording = null) =>
        _h.AddSession(InstructorId, courseId: courseId, startsAtUtc: starts, endsAtUtc: ends, status: status, title: title, recordingEpisodeId: recording);

    // ---- my-sessions ---------------------------------------------------------------------------------

    [Fact]
    public async Task MySessions_NotEnrolled_IsTheSharedNotFound()
    {
        var courseId = Guid.NewGuid();
        Session(courseId, LiveTestData.Now.AddDays(1));

        var result = await _h.LearnerQueries().GetMySessionsAsync(LearnerId, courseId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Same(LiveErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task MySessions_UnknownCourse_IsTheSameSharedNotFound()
    {
        var result = await _h.LearnerQueries().GetMySessionsAsync(LearnerId, Guid.NewGuid(), CancellationToken.None);

        Assert.Same(LiveErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task MySessions_OnlyTheCallersOwnEnrollmentCounts_AnotherLearnersDoesNot()
    {
        var courseId = Guid.NewGuid();
        Session(courseId, LiveTestData.Now.AddDays(1));
        _h.Enroll(Guid.NewGuid(), courseId); // someone else

        var result = await _h.LearnerQueries().GetMySessionsAsync(LearnerId, courseId, CancellationToken.None);

        Assert.Same(LiveErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task MySessions_ReturnsTheCourseSessionsSoonestFirst_IncludingCancelled_WithServerComputedStates()
    {
        var courseId = Guid.NewGuid();
        _h.Enroll(LearnerId, courseId);
        var now = LiveTestData.Now;

        var ended = Session(courseId, now.AddDays(-2), now.AddDays(-2).AddHours(2), title: "จบแล้ว");
        var live = Session(courseId, now.AddMinutes(5), title: "กำลังเปิดห้อง");
        var upcoming = Session(courseId, now.AddDays(3), title: "ยังไม่ถึง");
        var cancelled = Session(courseId, now.AddDays(1), status: LiveSessionStatus.Cancelled, title: "ยกเลิก");
        _h.Schedule.Contexts.Reverse(); // the store's own order must not leak through

        var result = await _h.LearnerQueries().GetMySessionsAsync(LearnerId, courseId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var response = result.Value;
        Assert.Equal(courseId, response.CourseId);
        Assert.Equal("test-course", response.CourseSlug);
        Assert.Equal("Asia/Bangkok", response.Timezone);
        Assert.Equal(now, response.ServerTimeUtc);

        // Soonest first (ended, live, cancelled, upcoming by start time), with every state computed by the server.
        Assert.Equal(
            [ended.SessionId, live.SessionId, cancelled.SessionId, upcoming.SessionId],
            response.Sessions.Select(s => s.Id).ToArray());
        Assert.Equal(
            [LiveSessionDisplayState.Ended, LiveSessionDisplayState.Live, LiveSessionDisplayState.Cancelled, LiveSessionDisplayState.Upcoming],
            response.Sessions.Select(s => s.DisplayState).ToArray());
    }

    [Fact]
    public async Task MySessions_CanJoinIsTrueOnlyWhileLive_AndJoinOpensAtIsStartMinusTheWindow()
    {
        var courseId = Guid.NewGuid();
        _h.Enroll(LearnerId, courseId);
        var now = LiveTestData.Now;

        var ended = Session(courseId, now.AddDays(-2), now.AddDays(-2).AddHours(2));
        var live = Session(courseId, now.AddMinutes(5));
        var upcoming = Session(courseId, now.AddDays(3));
        var cancelled = Session(courseId, now.AddMinutes(5).AddDays(1), status: LiveSessionStatus.Cancelled, title: "ยกเลิก");

        var response = (await _h.LearnerQueries().GetMySessionsAsync(LearnerId, courseId, CancellationToken.None)).Value;

        MySessionItem Item(Guid id) => response.Sessions.Single(s => s.Id == id);

        Assert.Equal(LiveSessionDisplayState.Live, Item(live.SessionId).DisplayState);
        Assert.True(Item(live.SessionId).CanJoin);
        Assert.False(Item(upcoming.SessionId).CanJoin);
        Assert.False(Item(ended.SessionId).CanJoin);
        Assert.False(Item(cancelled.SessionId).CanJoin);
        Assert.Equal(LiveSessionDisplayState.Ended, Item(ended.SessionId).DisplayState);
        Assert.Equal(LiveSessionDisplayState.Upcoming, Item(upcoming.SessionId).DisplayState);
        Assert.Equal(LiveSessionDisplayState.Cancelled, Item(cancelled.SessionId).DisplayState);

        Assert.Equal(upcoming.StartsAtUtc.AddMinutes(-15), Item(upcoming.SessionId).JoinOpensAtUtc);
        Assert.Equal("Scheduled", Item(live.SessionId).Status);
        Assert.Equal("Cancelled", Item(cancelled.SessionId).Status);
    }

    [Fact]
    public async Task MySessions_TheStateAtEveryBoundaryNeverDisagreesWithTheJoinGate()
    {
        var courseId = Guid.NewGuid();
        _h.Enroll(LearnerId, courseId);
        var starts = LiveTestData.Now.AddHours(1);
        var ends = starts.AddHours(2);
        var session = Session(courseId, starts, ends);
        _h.AddMeeting(session.SessionId);

        DateTime[] instants =
        [
            starts.AddMinutes(-15).AddTicks(-1),
            starts.AddMinutes(-15),
            starts.AddMinutes(-15).AddTicks(1),
            starts,
            ends.AddTicks(-1),
            ends,
            ends.AddTicks(1),
        ];

        foreach (var instant in instants)
        {
            _h.Clock.UtcNow = instant;

            var item = (await _h.LearnerQueries().GetMySessionsAsync(LearnerId, courseId, CancellationToken.None)).Value.Sessions.Single();
            var join = await _h.JoinService().JoinAsync(LearnerId, session.SessionId, null, null, null, CancellationToken.None);

            // canJoin <=> the join gate lets the learner in. The two surfaces share one calculator and one clock, so this must hold at every edge.
            Assert.Equal(item.CanJoin, join.IsSuccess);
        }
    }

    [Fact]
    public async Task MySessions_RoomReadyComesFromTheMeeting_AndInviteStatusFromTheCallersOwnInvite()
    {
        var courseId = Guid.NewGuid();
        _h.Enroll(LearnerId, courseId);
        var withRoom = Session(courseId, LiveTestData.Now.AddDays(1));
        var withoutRoom = Session(courseId, LiveTestData.Now.AddDays(2));
        var noRow = Session(courseId, LiveTestData.Now.AddDays(3));
        _h.AddMeeting(withRoom.SessionId);
        _h.Meetings.Add(SESSION_MEETING.Stage(withoutRoom.SessionId)); // exists but has no link
        _h.Invites.Invites[(withRoom.SessionId, LearnerId)] = (InviteStatus.Invited, LiveParticipantRole.Learner);
        _h.Invites.Invites[(withoutRoom.SessionId, LearnerId)] = (InviteStatus.Skipped, LiveParticipantRole.Learner);
        _h.Invites.Invites[(withRoom.SessionId, Guid.NewGuid())] = (InviteStatus.Cancelled, LiveParticipantRole.Learner); // another learner's invite

        var sessions = (await _h.LearnerQueries().GetMySessionsAsync(LearnerId, courseId, CancellationToken.None)).Value.Sessions;

        Assert.True(sessions.Single(s => s.Id == withRoom.SessionId).RoomReady);
        Assert.False(sessions.Single(s => s.Id == withoutRoom.SessionId).RoomReady);
        Assert.False(sessions.Single(s => s.Id == noRow.SessionId).RoomReady);
        Assert.Equal("Invited", sessions.Single(s => s.Id == withRoom.SessionId).InviteStatus);
        Assert.Equal("Skipped", sessions.Single(s => s.Id == withoutRoom.SessionId).InviteStatus);
        Assert.Null(sessions.Single(s => s.Id == noRow.SessionId).InviteStatus);
    }

    [Fact]
    public async Task MySessions_RecordingAndAttendanceFlags()
    {
        var courseId = Guid.NewGuid();
        _h.Enroll(LearnerId, courseId);
        var episode = Guid.NewGuid();
        var withRecording = Session(courseId, LiveTestData.Now.AddDays(-2), LiveTestData.Now.AddDays(-2).AddHours(2), recording: episode);
        var without = Session(courseId, LiveTestData.Now.AddDays(2));

        var before = (await _h.LearnerQueries().GetMySessionsAsync(LearnerId, courseId, CancellationToken.None)).Value;
        Assert.False(before.HasAttendedAnySession);

        _h.Attendance.Attended.Add((LearnerId, courseId));
        var after = (await _h.LearnerQueries().GetMySessionsAsync(LearnerId, courseId, CancellationToken.None)).Value;
        Assert.True(after.HasAttendedAnySession);

        var recorded = after.Sessions.Single(s => s.Id == withRecording.SessionId);
        Assert.True(recorded.HasRecording);
        Assert.Equal(episode, recorded.RecordingEpisodeId);
        var plain = after.Sessions.Single(s => s.Id == without.SessionId);
        Assert.False(plain.HasRecording);
        Assert.Null(plain.RecordingEpisodeId);
    }

    [Fact]
    public async Task MySessions_AttendanceOfAnotherUserOrAnotherCourse_DoesNotCount()
    {
        var courseId = Guid.NewGuid();
        _h.Enroll(LearnerId, courseId);
        Session(courseId, LiveTestData.Now.AddDays(1));
        _h.Attendance.Attended.Add((Guid.NewGuid(), courseId));
        _h.Attendance.Attended.Add((LearnerId, Guid.NewGuid()));

        var response = (await _h.LearnerQueries().GetMySessionsAsync(LearnerId, courseId, CancellationToken.None)).Value;

        Assert.False(response.HasAttendedAnySession);
    }

    [Fact]
    public async Task MySessions_ACourseWithNoSessionsYet_StillReturnsTheSlugFromTheCourseSummary()
    {
        var courseId = Guid.NewGuid();
        _h.Enroll(LearnerId, courseId);
        _h.CourseSummaries.Summaries[courseId] = new CourseSummaryInfo(courseId, "empty-course", "Empty", null, null);

        var result = await _h.LearnerQueries().GetMySessionsAsync(LearnerId, courseId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("empty-course", result.Value.CourseSlug);
        Assert.Empty(result.Value.Sessions);
    }

    [Fact]
    public async Task MySessions_AnEnrolledButUnknownCourseWithNoSessions_IsTheSharedNotFound()
    {
        var courseId = Guid.NewGuid();
        _h.Enroll(LearnerId, courseId); // no sessions and no course summary: nothing to show

        var result = await _h.LearnerQueries().GetMySessionsAsync(LearnerId, courseId, CancellationToken.None);

        Assert.Same(LiveErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task MySessions_OnlySessionsOfThatCourseAreReturned()
    {
        var courseId = Guid.NewGuid();
        _h.Enroll(LearnerId, courseId);
        var mine = Session(courseId, LiveTestData.Now.AddDays(1));
        Session(Guid.NewGuid(), LiveTestData.Now.AddDays(1)); // another course

        var sessions = (await _h.LearnerQueries().GetMySessionsAsync(LearnerId, courseId, CancellationToken.None)).Value.Sessions;

        Assert.Equal(mine.SessionId, Assert.Single(sessions).Id);
    }

    [Fact]
    public async Task MySessions_NeverCarriesTheRoomLink_NotEvenWhenARoomIsReady()
    {
        var courseId = Guid.NewGuid();
        _h.Enroll(LearnerId, courseId);
        var session = Session(courseId, LiveTestData.Now.AddMinutes(5));
        _h.AddMeeting(session.SessionId);

        var response = (await _h.LearnerQueries().GetMySessionsAsync(LearnerId, courseId, CancellationToken.None)).Value;
        var json = JsonSerializer.Serialize(response, Json);

        Assert.DoesNotContain("meet.google.com", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET-ROOM-TOKEN", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("meetUrl", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"roomReady\":true", json);
    }

    [Fact]
    public async Task MySessions_ReadsMeetingsAndInvitesInOneBatchEach_NotPerSession()
    {
        var courseId = Guid.NewGuid();
        _h.Enroll(LearnerId, courseId);
        for (var i = 0; i < 12; i++)
        {
            Session(courseId, LiveTestData.Now.AddDays(i + 1));
        }

        var meetings = new CountingMeetingRepository(_h.Meetings);
        var queries = new LiveLearnerQueries(
            _h.Schedule, _h.Learning, _h.CourseSummaries, meetings, _h.Invites, _h.Attendance, _h.JoinService(), _h.Clock, _h.Options);

        var response = (await queries.GetMySessionsAsync(LearnerId, courseId, CancellationToken.None)).Value;

        Assert.Equal(12, response.Sessions.Count);
        Assert.Equal(1, meetings.BatchReads);
    }

    // ---- upcoming --------------------------------------------------------------------------------------

    [Fact]
    public async Task Upcoming_NoEnrollments_IsEmpty_WithTheServerTime()
    {
        var response = await _h.LearnerQueries().GetUpcomingAsync(LearnerId, 10, CancellationToken.None);

        Assert.Empty(response.Items);
        Assert.Equal(LiveTestData.Now, response.ServerTimeUtc);
    }

    [Fact]
    public async Task Upcoming_ListsOnlyUnfinishedUncancelledSessionsOfActivelyEnrolledCourses_SoonestFirst()
    {
        var now = LiveTestData.Now;
        var courseA = Guid.NewGuid();
        var courseB = Guid.NewGuid();
        var notMine = Guid.NewGuid();
        _h.Enroll(LearnerId, courseA);
        _h.Enroll(LearnerId, courseB);

        var later = Session(courseA, now.AddDays(5), title: "B");
        var soon = Session(courseB, now.AddDays(1), title: "A");
        var live = Session(courseA, now.AddMinutes(-30), now.AddMinutes(90), title: "now");
        Session(courseA, now.AddDays(-3), now.AddDays(-3).AddHours(2), title: "ended");
        Session(courseA, now.AddDays(2), status: LiveSessionStatus.Cancelled, title: "cancelled");
        Session(notMine, now.AddDays(1), title: "other course"); // not enrolled

        var response = await _h.LearnerQueries().GetUpcomingAsync(LearnerId, 10, CancellationToken.None);

        Assert.Equal([live.SessionId, soon.SessionId, later.SessionId], response.Items.Select(i => i.SessionId).ToArray());

        var running = response.Items[0];
        Assert.Equal(LiveSessionDisplayState.Live, running.DisplayState);
        Assert.True(running.CanJoin);
        Assert.Equal("test-course", running.CourseSlug);
        Assert.Equal("คอร์สทดสอบ", running.CourseTitle);
        Assert.Equal(running.StartsAtUtc.AddMinutes(-15), running.JoinOpensAtUtc);

        Assert.Equal(LiveSessionDisplayState.Upcoming, response.Items[1].DisplayState);
        Assert.False(response.Items[1].CanJoin);
    }

    [Fact]
    public async Task Upcoming_AnExpiredOrRevokedEnrollmentContributesNothing()
    {
        var active = Guid.NewGuid();
        var lapsed = Guid.NewGuid();
        _h.Enroll(LearnerId, active); // `lapsed` is deliberately not enrolled: the contract reports only active, unexpired enrollments
        var kept = Session(active, LiveTestData.Now.AddDays(1));
        Session(lapsed, LiveTestData.Now.AddDays(1));

        var response = await _h.LearnerQueries().GetUpcomingAsync(LearnerId, 10, CancellationToken.None);

        Assert.Equal(kept.SessionId, Assert.Single(response.Items).SessionId);
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(10, 10)]
    [InlineData(20, 20)]
    [InlineData(21, 20)]
    [InlineData(1000, 20)]
    [InlineData(0, 10)]
    [InlineData(-3, 10)]
    public async Task Upcoming_TheLimitIsClampedToOneToTwenty_AndANonPositiveValueMeansTheDefaultOfTen(int requested, int expectedCount)
    {
        var courseId = Guid.NewGuid();
        _h.Enroll(LearnerId, courseId);
        for (var i = 0; i < 30; i++)
        {
            Session(courseId, LiveTestData.Now.AddDays(i + 1));
        }

        var response = await _h.LearnerQueries().GetUpcomingAsync(LearnerId, requested, CancellationToken.None);

        Assert.Equal(expectedCount, response.Items.Count);
    }

    [Fact]
    public async Task Upcoming_NeverCarriesTheRoomLink()
    {
        var courseId = Guid.NewGuid();
        _h.Enroll(LearnerId, courseId);
        var session = Session(courseId, LiveTestData.Now.AddMinutes(5));
        _h.AddMeeting(session.SessionId);

        var json = JsonSerializer.Serialize(await _h.LearnerQueries().GetUpcomingAsync(LearnerId, 10, CancellationToken.None), Json);

        Assert.DoesNotContain("meet.google.com", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("meetUrl", json, StringComparison.OrdinalIgnoreCase);
    }

    // ---- calendar.ics ----------------------------------------------------------------------------------

    private static string Unfold(string ics) => ics.Replace("\r\n ", string.Empty, StringComparison.Ordinal);

    [Fact]
    public async Task Calendar_NotEntitled_IsTheSharedNotFound()
    {
        var session = _h.AddSession(InstructorId);

        var result = await _h.LearnerQueries().GetCalendarAsync(LearnerId, session.SessionId, CancellationToken.None);

        Assert.Same(LiveErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Calendar_UnknownSession_IsTheSharedNotFound()
    {
        var result = await _h.LearnerQueries().GetCalendarAsync(LearnerId, Guid.NewGuid(), CancellationToken.None);

        Assert.Same(LiveErrors.NotFound, result.Error);
    }

    [Fact]
    public async Task Calendar_EntitlementIsCheckedBeforeCancelledOrEnded_SoAStrangerGetsNo409()
    {
        var cancelled = _h.AddSession(InstructorId, status: LiveSessionStatus.Cancelled);
        var ended = _h.AddSession(InstructorId, startsAtUtc: LiveTestData.Now.AddDays(-2), endsAtUtc: LiveTestData.Now.AddDays(-2).AddHours(2));

        Assert.Same(LiveErrors.NotFound, (await _h.LearnerQueries().GetCalendarAsync(LearnerId, cancelled.SessionId, CancellationToken.None)).Error);
        Assert.Same(LiveErrors.NotFound, (await _h.LearnerQueries().GetCalendarAsync(LearnerId, ended.SessionId, CancellationToken.None)).Error);
    }

    [Fact]
    public async Task Calendar_EnrolledLearner_GetsAPublishFileForThatSessionOnly_PointingBackAtThePlatform()
    {
        var courseId = Guid.NewGuid();
        _h.Enroll(LearnerId, courseId);
        var session = Session(courseId, LiveTestData.Now.AddDays(2), title: "คาบพิเศษ");
        var other = Session(courseId, LiveTestData.Now.AddDays(3), title: "คาบอื่น");
        var meeting = _h.AddMeeting(session.SessionId);
        meeting.MarkSessionChanged(); // ICS_SEQUENCE -> 1
        meeting.MarkSessionChanged(); // -> 2

        var result = await _h.LearnerQueries().GetCalendarAsync(LearnerId, session.SessionId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal($"live-{session.SessionId:N}.ics", result.Value.FileName);

        var ics = Unfold(result.Value.Content);
        Assert.Contains("METHOD:PUBLISH", ics);
        Assert.Contains($"UID:{session.SessionId:N}@app.example.test", ics);
        Assert.Contains($"SEQUENCE:{meeting.ICS_SEQUENCE}", ics);
        Assert.Equal(2, meeting.ICS_SEQUENCE);
        Assert.Contains($"URL:https://app.example.test/live/{session.SessionId}/join", ics);
        Assert.Contains("คาบพิเศษ", ics);
        Assert.Equal(1, ics.Split("BEGIN:VEVENT").Length - 1);
        Assert.DoesNotContain(other.SessionId.ToString("N"), ics);
        Assert.DoesNotContain("ATTENDEE", ics); // PUBLISH has no attendee
    }

    [Fact]
    public async Task Calendar_NeverContainsTheRoomLink_EvenWhenTheMeetingHasOne()
    {
        var courseId = Guid.NewGuid();
        _h.Enroll(LearnerId, courseId);
        var session = Session(courseId, LiveTestData.Now.AddDays(2));
        _h.AddMeeting(session.SessionId);

        var ics = Unfold((await _h.LearnerQueries().GetCalendarAsync(LearnerId, session.SessionId, CancellationToken.None)).Value.Content);

        Assert.DoesNotContain("meet.google.com", ics, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET-ROOM-TOKEN", ics, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Calendar_ASessionWithoutAMeetingRow_UsesSequenceZero()
    {
        var courseId = Guid.NewGuid();
        _h.Enroll(LearnerId, courseId);
        var session = Session(courseId, LiveTestData.Now.AddDays(2));

        var ics = Unfold((await _h.LearnerQueries().GetCalendarAsync(LearnerId, session.SessionId, CancellationToken.None)).Value.Content);

        Assert.Contains("SEQUENCE:0", ics);
    }

    [Fact]
    public async Task Calendar_TheOwningInstructorMayDownloadTheirOwnSession()
    {
        var session = _h.AddSession(InstructorId, startsAtUtc: LiveTestData.Now.AddDays(2));

        var result = await _h.LearnerQueries().GetCalendarAsync(InstructorId, session.SessionId, CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task Calendar_CancelledSession_Is409Cancelled()
    {
        var courseId = Guid.NewGuid();
        _h.Enroll(LearnerId, courseId);
        var session = Session(courseId, LiveTestData.Now.AddDays(2), status: LiveSessionStatus.Cancelled);

        var result = await _h.LearnerQueries().GetCalendarAsync(LearnerId, session.SessionId, CancellationToken.None);

        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(LiveReasons.SessionCancelled, result.Error.Reason);
    }

    [Fact]
    public async Task Calendar_EndedSession_Is409Ended_IncludingTheInstantItEnds()
    {
        var courseId = Guid.NewGuid();
        _h.Enroll(LearnerId, courseId);
        var now = LiveTestData.Now;
        var endsNow = Session(courseId, now.AddHours(-2), now);
        var past = Session(courseId, now.AddDays(-2), now.AddDays(-2).AddHours(2));
        var oneTickLeft = Session(courseId, now.AddHours(-2), now.AddTicks(1));

        var atEnd = await _h.LearnerQueries().GetCalendarAsync(LearnerId, endsNow.SessionId, CancellationToken.None);
        var wellPast = await _h.LearnerQueries().GetCalendarAsync(LearnerId, past.SessionId, CancellationToken.None);
        var almost = await _h.LearnerQueries().GetCalendarAsync(LearnerId, oneTickLeft.SessionId, CancellationToken.None);

        Assert.Equal(LiveReasons.SessionEnded, atEnd.Error.Reason); // EndsAtUtc <= now
        Assert.Equal(LiveReasons.SessionEnded, wellPast.Error.Reason);
        Assert.True(almost.IsSuccess);
    }

    /// <summary>Counts how many times the meetings are read in bulk, delegating to the harness' in-memory repository.</summary>
    private sealed class CountingMeetingRepository(ISessionMeetingRepository inner) : ISessionMeetingRepository
    {
        public int BatchReads { get; private set; }

        public Task<SESSION_MEETING?> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken) =>
            inner.GetBySessionIdAsync(sessionId, cancellationToken);

        public Task<IReadOnlyList<SESSION_MEETING>> GetBySessionIdsAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken)
        {
            BatchReads++;
            return inner.GetBySessionIdsAsync(sessionIds, cancellationToken);
        }

        public Task<IReadOnlySet<Guid>> GetExistingSessionIdsAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken) =>
            inner.GetExistingSessionIdsAsync(sessionIds, cancellationToken);

        public Task<IReadOnlyList<Guid>> GetDueSessionIdsAsync(DateTime nowUtc, int take, CancellationToken cancellationToken) =>
            inner.GetDueSessionIdsAsync(nowUtc, take, cancellationToken);

        public Task<IReadOnlyList<SESSION_MEETING>> GetResettableByInstructorAsync(Guid instructorUserId, CancellationToken cancellationToken) =>
            inner.GetResettableByInstructorAsync(instructorUserId, cancellationToken);

        public void Add(SESSION_MEETING meeting) => inner.Add(meeting);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => inner.SaveChangesAsync(cancellationToken);

        public void ClearTracking() => inner.ClearTracking();
    }
}
