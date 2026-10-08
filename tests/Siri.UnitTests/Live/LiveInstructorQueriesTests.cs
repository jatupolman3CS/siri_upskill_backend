using System.Text.Json;
using System.Text.Json.Serialization;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Contracts;
using Siri.Modules.Live.Domain;

namespace Siri.UnitTests.Live;

/// <summary>
/// The instructor's read side (docs/contracts/P11-05-live-learner-instructor-api-join-gate.md section 4.4): their sessions, one session's detail (the only response
/// besides the join gate that may carry the room link) and the roster. The tests pin ownership (403/404, no administrator bypass), the scope semantics, paging bounds,
/// batching, and that a roster never shows a full e-mail address.
/// </summary>
public class LiveInstructorQueriesTests
{
    private static readonly Guid InstructorId = Guid.NewGuid();
    private static readonly Guid OtherInstructorId = Guid.NewGuid();

    private static readonly JsonSerializerOptions Json = new() { Converters = { new JsonStringEnumConverter() }, PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    private readonly JoinHarness _h = new();

    private LiveSessionContext Own(DateTime starts, DateTime? ends = null, LiveSessionStatus status = LiveSessionStatus.Scheduled, Guid? courseId = null, string title = "คาบ") =>
        _h.AddSession(InstructorId, courseId: courseId, startsAtUtc: starts, endsAtUtc: ends, status: status, title: title);

    private void Joined(Guid sessionId, Guid courseId, Guid userId, DateTime at, LiveParticipantRole role = LiveParticipantRole.Learner) =>
        _h.JoinLogs.Committed.Add(SESSION_JOIN_LOG.Record(sessionId, courseId, userId, role, null, at, null, null));

    // ---- Session list --------------------------------------------------------------------------------

    [Fact]
    public async Task Sessions_Upcoming_ListsTheCallersNotFinishedUncancelledSessions_SoonestFirst_AndNobodyElses()
    {
        var now = LiveTestData.Now;
        var running = Own(now.AddMinutes(-30), now.AddMinutes(90), title: "running");
        var soon = Own(now.AddDays(1), title: "soon");
        var later = Own(now.AddDays(9), title: "later");
        Own(now.AddDays(-5), now.AddDays(-5).AddHours(2), title: "over");
        Own(now.AddDays(2), status: LiveSessionStatus.Cancelled, title: "cancelled");
        _h.AddSession(OtherInstructorId, startsAtUtc: now.AddDays(1), title: "not mine");

        var page = await _h.InstructorQueries().GetSessionsAsync(InstructorId, InstructorSessionScope.Upcoming, null, 1, 20, CancellationToken.None);

        Assert.Equal([running.SessionId, soon.SessionId, later.SessionId], page.Items.Select(i => i.SessionId).ToArray());
        Assert.Equal(3, page.TotalCount);
        Assert.Equal(LiveSessionDisplayState.Live, page.Items[0].DisplayState);
        Assert.Equal(LiveSessionDisplayState.Upcoming, page.Items[1].DisplayState);
    }

    [Fact]
    public async Task Sessions_Past_ListsSessionsThatAlreadyStarted_IncludingCancelled_LatestFirst()
    {
        var now = LiveTestData.Now;
        var older = Own(now.AddDays(-10), now.AddDays(-10).AddHours(2), title: "older");
        var recent = Own(now.AddDays(-2), now.AddDays(-2).AddHours(2), title: "recent");
        var cancelledPast = Own(now.AddDays(-4), now.AddDays(-4).AddHours(2), LiveSessionStatus.Cancelled, title: "cancelled past");
        Own(now.AddDays(3), title: "future");

        var page = await _h.InstructorQueries().GetSessionsAsync(InstructorId, InstructorSessionScope.Past, null, 1, 20, CancellationToken.None);

        Assert.Equal([recent.SessionId, cancelledPast.SessionId, older.SessionId], page.Items.Select(i => i.SessionId).ToArray());
        Assert.Equal(LiveSessionDisplayState.Cancelled, page.Items[1].DisplayState);
        Assert.All(page.Items.Where(i => i.DisplayState != LiveSessionDisplayState.Cancelled), i => Assert.Equal(LiveSessionDisplayState.Ended, i.DisplayState));
    }

    [Fact]
    public async Task Sessions_All_ListsEverything_LatestFirst()
    {
        var now = LiveTestData.Now;
        var past = Own(now.AddDays(-3), now.AddDays(-3).AddHours(2));
        var future = Own(now.AddDays(3));
        var cancelled = Own(now.AddDays(1), status: LiveSessionStatus.Cancelled);

        var page = await _h.InstructorQueries().GetSessionsAsync(InstructorId, InstructorSessionScope.All, null, 1, 20, CancellationToken.None);

        Assert.Equal([future.SessionId, cancelled.SessionId, past.SessionId], page.Items.Select(i => i.SessionId).ToArray());
    }

    [Fact]
    public async Task Sessions_FilteredByCourse_OnlyThatCourse_AndSomeoneElsesCourseMatchesNothing()
    {
        var now = LiveTestData.Now;
        var courseA = Guid.NewGuid();
        var courseB = Guid.NewGuid();
        var inA = Own(now.AddDays(1), courseId: courseA);
        Own(now.AddDays(2), courseId: courseB);
        var theirCourse = Guid.NewGuid();
        _h.AddSession(OtherInstructorId, courseId: theirCourse, startsAtUtc: now.AddDays(1));

        var onlyA = await _h.InstructorQueries().GetSessionsAsync(InstructorId, InstructorSessionScope.Upcoming, courseA, 1, 20, CancellationToken.None);
        var theirs = await _h.InstructorQueries().GetSessionsAsync(InstructorId, InstructorSessionScope.Upcoming, theirCourse, 1, 20, CancellationToken.None);

        Assert.Equal(inA.SessionId, Assert.Single(onlyA.Items).SessionId);
        Assert.Empty(theirs.Items);
        Assert.Equal(0, theirs.TotalCount); // never a count of another instructor's sessions
    }

    [Fact]
    public async Task Sessions_PagingIsBoundedAndClamped()
    {
        var now = LiveTestData.Now;
        for (var i = 0; i < 60; i++)
        {
            Own(now.AddDays(i + 1));
        }

        var q = _h.InstructorQueries();
        var defaultPage = await q.GetSessionsAsync(InstructorId, InstructorSessionScope.Upcoming, null, 1, 0, CancellationToken.None);
        var tooBig = await q.GetSessionsAsync(InstructorId, InstructorSessionScope.Upcoming, null, 1, 500, CancellationToken.None);
        var secondOfTen = await q.GetSessionsAsync(InstructorId, InstructorSessionScope.Upcoming, null, 2, 10, CancellationToken.None);
        var badPage = await q.GetSessionsAsync(InstructorId, InstructorSessionScope.Upcoming, null, -4, 10, CancellationToken.None);

        Assert.Equal(20, defaultPage.PageSize);
        Assert.Equal(20, defaultPage.Items.Count);
        Assert.Equal(50, tooBig.PageSize);
        Assert.Equal(50, tooBig.Items.Count);
        Assert.Equal(60, tooBig.TotalCount);
        Assert.Equal(2, secondOfTen.Page);
        Assert.Equal(10, secondOfTen.Items.Count);
        Assert.Equal(6, secondOfTen.TotalPages);
        Assert.Equal(1, badPage.Page);

        // Page two is the ten after page one — no overlap.
        var firstOfTen = await q.GetSessionsAsync(InstructorId, InstructorSessionScope.Upcoming, null, 1, 10, CancellationToken.None);
        Assert.Empty(firstOfTen.Items.Select(i => i.SessionId).Intersect(secondOfTen.Items.Select(i => i.SessionId)));
    }

    [Fact]
    public async Task Sessions_AttendanceAndMeetingsAreReadInOneBatch_ForThePageOnly()
    {
        var now = LiveTestData.Now;
        var a = Own(now.AddDays(1));
        var b = Own(now.AddDays(2));
        for (var i = 0; i < 5; i++)
        {
            Own(now.AddDays(10 + i));
        }

        _h.Attendance.Stats[a.SessionId] = new LiveSessionStats(a.SessionId, ExpectedLearners: 12, JoinedLearners: 7, MeetingUsable: true);
        _h.AddMeeting(a.SessionId);
        _h.Meetings.Add(SESSION_MEETING.Stage(b.SessionId));

        var page = await _h.InstructorQueries().GetSessionsAsync(InstructorId, InstructorSessionScope.Upcoming, null, 1, 2, CancellationToken.None);

        Assert.Equal(1, _h.Attendance.StatsCalls);
        Assert.Equal(2, _h.Attendance.StatsRequests.Single().Count);

        var first = page.Items[0];
        Assert.Equal(12, first.ExpectedLearners);
        Assert.Equal(7, first.JoinedLearners);
        Assert.True(first.Meeting.HasMeetingLink);
        Assert.True(first.Meeting.IsUsable);
        Assert.Equal(MeetingNeedsAction.None, first.Meeting.NeedsAction);

        var second = page.Items[1];
        Assert.Equal(0, second.ExpectedLearners);
        Assert.False(second.Meeting.HasMeetingLink);
        Assert.Equal(MeetingNeedsAction.Waiting, second.Meeting.NeedsAction);
    }

    [Fact]
    public async Task Sessions_ASessionWithoutAMeetingRow_ReadsAsWaiting_NotMissing()
    {
        var session = Own(LiveTestData.Now.AddDays(1));

        var page = await _h.InstructorQueries().GetSessionsAsync(InstructorId, InstructorSessionScope.Upcoming, null, 1, 20, CancellationToken.None);

        var meeting = Assert.Single(page.Items).Meeting;
        Assert.Equal(session.SessionId, meeting.SessionId);
        Assert.Equal(MeetingSyncStatus.Pending, meeting.SyncStatus);
        Assert.Equal(MeetingNeedsAction.Waiting, meeting.NeedsAction);
        Assert.False(meeting.IsUsable);
    }

    [Fact]
    public async Task Sessions_NoSessions_IsAnEmptyPage_AndNoStatsAreRequested()
    {
        var page = await _h.InstructorQueries().GetSessionsAsync(InstructorId, InstructorSessionScope.Upcoming, null, 1, 20, CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Equal(0, _h.Attendance.StatsCalls);
    }

    [Fact]
    public async Task Sessions_TheListNeverCarriesTheRoomLink()
    {
        var session = Own(LiveTestData.Now.AddDays(1));
        _h.AddMeeting(session.SessionId);

        var page = await _h.InstructorQueries().GetSessionsAsync(InstructorId, InstructorSessionScope.Upcoming, null, 1, 20, CancellationToken.None);
        var json = JsonSerializer.Serialize(page, Json);

        Assert.DoesNotContain("meet.google.com", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SECRET-ROOM-TOKEN", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("meetUrl", json, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("\"hasMeetingLink\":true", json);
    }

    // ---- Session detail --------------------------------------------------------------------------------

    [Fact]
    public async Task Detail_UnknownSession_Is404()
    {
        var result = await _h.InstructorQueries().GetSessionDetailAsync(InstructorId, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task Detail_SomeoneElsesSession_Is403_AndAnAdministratorIsJustSomeoneElse()
    {
        var session = _h.AddSession(OtherInstructorId, startsAtUtc: LiveTestData.Now.AddDays(1));
        _h.AddMeeting(session.SessionId);
        var administrator = Guid.NewGuid();

        var other = await _h.InstructorQueries().GetSessionDetailAsync(InstructorId, session.SessionId, CancellationToken.None);
        var admin = await _h.InstructorQueries().GetSessionDetailAsync(administrator, session.SessionId, CancellationToken.None);

        Assert.Equal("forbidden", other.Error.Code);
        Assert.Equal("forbidden", admin.Error.Code);
        Assert.DoesNotContain("meet.google.com", other.Error.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Detail_TheOwner_SeesTheRoomLink_AndAllTheNumbers()
    {
        var courseId = Guid.NewGuid();
        var now = LiveTestData.Now;
        var session = Own(now.AddDays(1), courseId: courseId, title: "คาบหนึ่ง");
        _h.AddMeeting(session.SessionId);
        _h.Enroll(Guid.NewGuid(), courseId);
        _h.Enroll(Guid.NewGuid(), courseId);
        _h.Enroll(InstructorId, courseId); // the owner is never counted as a learner of their own course
        _h.Attendance.Stats[session.SessionId] = new LiveSessionStats(session.SessionId, ExpectedLearners: 5, JoinedLearners: 3, MeetingUsable: true);

        var detail = (await _h.InstructorQueries().GetSessionDetailAsync(InstructorId, session.SessionId, CancellationToken.None)).Value;

        Assert.Equal(JoinHarness.RoomUrl, detail.MeetUrl);
        Assert.Equal(session.SessionId, detail.SessionId);
        Assert.Equal(courseId, detail.CourseId);
        Assert.Equal("test-course", detail.CourseSlug);
        Assert.Equal("คอร์สทดสอบ", detail.CourseTitle);
        Assert.Equal("คาบหนึ่ง", detail.Title);
        Assert.Equal("Scheduled", detail.Status);
        Assert.Equal(LiveSessionDisplayState.Upcoming, detail.DisplayState);
        Assert.Equal(2, detail.EnrolledCount);
        Assert.Equal(5, detail.ExpectedLearners);
        Assert.Equal(3, detail.JoinedLearners);
        Assert.Equal(now, detail.ServerTimeUtc);
        Assert.True(detail.Meeting.HasMeetingLink);
        Assert.True(detail.Meeting.IsUsable);
    }

    [Fact]
    public async Task Detail_NoRoomYet_HasANullLink_AndAWaitingSummary()
    {
        var session = Own(LiveTestData.Now.AddDays(1));

        var detail = (await _h.InstructorQueries().GetSessionDetailAsync(InstructorId, session.SessionId, CancellationToken.None)).Value;

        Assert.Null(detail.MeetUrl);
        Assert.Equal(MeetingNeedsAction.Waiting, detail.Meeting.NeedsAction);
    }

    [Fact]
    public async Task Detail_ADeletedRoom_ShowsNoLink_EvenIfAnOldCiphertextIsStillStored()
    {
        var session = Own(LiveTestData.Now.AddDays(1), status: LiveSessionStatus.Cancelled);
        var meeting = _h.AddMeeting(session.SessionId);
        meeting.MarkSessionCancelled();

        var detail = (await _h.InstructorQueries().GetSessionDetailAsync(InstructorId, session.SessionId, CancellationToken.None)).Value;

        Assert.Null(detail.MeetUrl);
        Assert.Equal(LiveSessionDisplayState.Cancelled, detail.DisplayState);
    }

    [Fact]
    public async Task Detail_AnUnreadableStoredLink_IsNullNotAnException()
    {
        var session = Own(LiveTestData.Now.AddDays(1));
        var meeting = SESSION_MEETING.Stage(session.SessionId);
        meeting.SetManualLink(LiveTestData.Protector().Encrypt(JoinHarness.RoomUrl)); // another key
        _h.Meetings.Add(meeting);

        var detail = (await _h.InstructorQueries().GetSessionDetailAsync(InstructorId, session.SessionId, CancellationToken.None)).Value;

        Assert.Null(detail.MeetUrl);
        Assert.True(detail.Meeting.HasMeetingLink);
    }

    // ---- Roster -----------------------------------------------------------------------------------------

    private (LiveSessionContext Session, Guid[] Learners) RosterFixture(int learnerCount = 3)
    {
        var courseId = Guid.NewGuid();
        var session = Own(LiveTestData.Now.AddDays(1), courseId: courseId);
        var learners = Enumerable.Range(0, learnerCount).Select(_ => Guid.NewGuid()).ToArray();
        foreach (var learner in learners)
        {
            _h.Enroll(learner, courseId);
        }

        return (session, learners);
    }

    [Fact]
    public async Task Roster_UnknownSession_Is404_AndSomeoneElsesIs403()
    {
        var theirs = _h.AddSession(OtherInstructorId, startsAtUtc: LiveTestData.Now.AddDays(1));

        var unknown = await _h.InstructorQueries().GetRosterAsync(InstructorId, Guid.NewGuid(), RosterFilter.All, 1, 50, CancellationToken.None);
        var forbidden = await _h.InstructorQueries().GetRosterAsync(InstructorId, theirs.SessionId, RosterFilter.All, 1, 50, CancellationToken.None);

        Assert.Equal("not_found", unknown.Error.Code);
        Assert.Equal("forbidden", forbidden.Error.Code);
        Assert.Empty(_h.Contacts.BatchRequests); // nothing about anyone was read
    }

    [Fact]
    public async Task Roster_IsEnrolledPlusInvitedPlusEveryoneWhoJoined_WithoutTheInstructor_AndWithoutDuplicates()
    {
        var (session, learners) = RosterFixture(learnerCount: 2);
        var invitedOnly = Guid.NewGuid();
        var joinedThenLeft = Guid.NewGuid(); // no longer enrolled, but they did get the link
        _h.Invites.Invites[(session.SessionId, invitedOnly)] = (InviteStatus.Invited, LiveParticipantRole.Learner);
        _h.Invites.Invites[(session.SessionId, learners[0])] = (InviteStatus.Invited, LiveParticipantRole.Learner); // also enrolled
        _h.Invites.Invites[(session.SessionId, InstructorId)] = (InviteStatus.Invited, LiveParticipantRole.Instructor);
        Joined(session.SessionId, session.CourseId, joinedThenLeft, LiveTestData.Now.AddMinutes(-3));
        Joined(session.SessionId, session.CourseId, InstructorId, LiveTestData.Now.AddMinutes(-9), LiveParticipantRole.Instructor);

        var page = (await _h.InstructorQueries().GetRosterAsync(InstructorId, session.SessionId, RosterFilter.All, 1, 50, CancellationToken.None)).Value;

        var ids = page.Items.Select(i => i.UserId).ToHashSet();
        Assert.Equal(4, page.TotalCount);
        Assert.Equal(4, ids.Count);
        Assert.Contains(learners[0], ids);
        Assert.Contains(learners[1], ids);
        Assert.Contains(invitedOnly, ids);
        Assert.Contains(joinedThenLeft, ids);
        Assert.DoesNotContain(InstructorId, ids);
    }

    [Fact]
    public async Task Roster_FiltersJoinedAndNotJoined()
    {
        var (session, learners) = RosterFixture(learnerCount: 4);
        Joined(session.SessionId, session.CourseId, learners[0], LiveTestData.Now.AddMinutes(-5));
        Joined(session.SessionId, session.CourseId, learners[1], LiveTestData.Now.AddMinutes(-4));

        var joined = (await _h.InstructorQueries().GetRosterAsync(InstructorId, session.SessionId, RosterFilter.Joined, 1, 50, CancellationToken.None)).Value;
        var notJoined = (await _h.InstructorQueries().GetRosterAsync(InstructorId, session.SessionId, RosterFilter.NotJoined, 1, 50, CancellationToken.None)).Value;
        var all = (await _h.InstructorQueries().GetRosterAsync(InstructorId, session.SessionId, RosterFilter.All, 1, 50, CancellationToken.None)).Value;

        Assert.Equal(new[] { learners[0], learners[1] }.Order(), joined.Items.Select(i => i.UserId).Order());
        Assert.All(joined.Items, i => Assert.True(i.Joined));
        Assert.Equal(new[] { learners[2], learners[3] }.Order(), notJoined.Items.Select(i => i.UserId).Order());
        Assert.All(notJoined.Items, i => Assert.False(i.Joined));
        Assert.Equal(4, all.TotalCount);
        Assert.Equal(2, joined.TotalCount);
        Assert.Equal(2, notJoined.TotalCount);
    }

    [Fact]
    public async Task Roster_ReportsFirstJoinTimeAndJoinCount_OnlyFromLearnerRows()
    {
        var (session, learners) = RosterFixture(learnerCount: 2);
        var first = LiveTestData.Now.AddMinutes(-20);
        Joined(session.SessionId, session.CourseId, learners[0], first.AddMinutes(10));
        Joined(session.SessionId, session.CourseId, learners[0], first);
        Joined(session.SessionId, session.CourseId, learners[0], first.AddMinutes(5));
        Joined(session.SessionId, session.CourseId, learners[1], first, LiveParticipantRole.Instructor); // a role mix-up must not count

        var page = (await _h.InstructorQueries().GetRosterAsync(InstructorId, session.SessionId, RosterFilter.All, 1, 50, CancellationToken.None)).Value;

        var who = page.Items.Single(i => i.UserId == learners[0]);
        Assert.True(who.Joined);
        Assert.Equal(first, who.FirstJoinedAtUtc);
        Assert.Equal(3, who.JoinCount);

        var other = page.Items.Single(i => i.UserId == learners[1]);
        Assert.False(other.Joined);
        Assert.Null(other.FirstJoinedAtUtc);
        Assert.Equal(0, other.JoinCount);
    }

    [Fact]
    public async Task Roster_ShowsADisplayNameAndAMaskedEmailOnly_NeverTheFullAddress()
    {
        var (session, learners) = RosterFixture(learnerCount: 2);
        _h.Contacts.Contacts[learners[0]] = ("somchai.jaidee@gmail.com", "สมชาย ใจดี");
        _h.Contacts.Contacts[learners[1]] = ("malee@example.co.th", "มาลี");

        var page = (await _h.InstructorQueries().GetRosterAsync(InstructorId, session.SessionId, RosterFilter.All, 1, 50, CancellationToken.None)).Value;
        var json = JsonSerializer.Serialize(page, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });

        var somchai = page.Items.Single(i => i.UserId == learners[0]);
        Assert.Equal("สมชาย ใจดี", somchai.DisplayName);
        Assert.Equal("s***@g***.com", somchai.EmailMasked);
        Assert.Equal("m***@e***.th", page.Items.Single(i => i.UserId == learners[1]).EmailMasked);

        Assert.DoesNotContain("somchai.jaidee", json);
        Assert.DoesNotContain("gmail.com", json);
        Assert.DoesNotContain("malee@", json);
        Assert.DoesNotContain("example.co.th", json);
    }

    [Fact]
    public async Task Roster_AUserWhoseContactCannotBeFound_ShowsNullNameAndEmail_AndSortsLast()
    {
        var (session, learners) = RosterFixture(learnerCount: 3);
        _h.Contacts.Contacts[learners[0]] = ("b@x.com", "Bravo");
        _h.Contacts.Contacts[learners[1]] = ("a@x.com", "alpha");
        // learners[2] has no contact (e.g. the account was deleted)

        var page = (await _h.InstructorQueries().GetRosterAsync(InstructorId, session.SessionId, RosterFilter.All, 1, 50, CancellationToken.None)).Value;

        Assert.Equal([learners[1], learners[0], learners[2]], page.Items.Select(i => i.UserId).ToArray()); // alpha, Bravo (case-insensitive), unknown last
        Assert.Null(page.Items[2].DisplayName);
        Assert.Null(page.Items[2].EmailMasked);
    }

    [Fact]
    public async Task Roster_IsSortedByDisplayName_ThenPaged_AndTheContactsAreReadOnceForTheWholeSelection()
    {
        var (session, learners) = RosterFixture(learnerCount: 25);
        for (var i = 0; i < learners.Length; i++)
        {
            _h.Contacts.Contacts[learners[i]] = ($"u{i}@x.com", $"Name {i:D2}");
        }

        var queries = _h.InstructorQueries();
        var page1 = (await queries.GetRosterAsync(InstructorId, session.SessionId, RosterFilter.All, 1, 10, CancellationToken.None)).Value;
        var page3 = (await queries.GetRosterAsync(InstructorId, session.SessionId, RosterFilter.All, 3, 10, CancellationToken.None)).Value;

        Assert.Equal(25, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);
        Assert.Equal(Enumerable.Range(0, 10).Select(i => $"Name {i:D2}"), page1.Items.Select(i => i.DisplayName));
        Assert.Equal(5, page3.Items.Count);
        Assert.Equal(Enumerable.Range(20, 5).Select(i => $"Name {i:D2}"), page3.Items.Select(i => i.DisplayName));

        // One batched contact lookup per call (never one per user).
        Assert.Equal(2, _h.Contacts.BatchRequests.Count);
        Assert.All(_h.Contacts.BatchRequests, request => Assert.Equal(25, request.Count));
    }

    [Fact]
    public async Task Roster_PageSizeIsClamped_AndABadPageIsPageOne()
    {
        var (session, _) = RosterFixture(learnerCount: 3);
        var queries = _h.InstructorQueries();

        var defaulted = (await queries.GetRosterAsync(InstructorId, session.SessionId, RosterFilter.All, 0, 0, CancellationToken.None)).Value;
        var capped = (await queries.GetRosterAsync(InstructorId, session.SessionId, RosterFilter.All, 1, 100000, CancellationToken.None)).Value;

        Assert.Equal(1, defaulted.Page);
        Assert.Equal(50, defaulted.PageSize);
        Assert.Equal(100, capped.PageSize);
    }

    [Fact]
    public async Task Roster_MapsTheInviteStatus_AndLeavesItNullForNoInvite()
    {
        var (session, learners) = RosterFixture(learnerCount: 3);
        _h.Invites.Invites[(session.SessionId, learners[0])] = (InviteStatus.Invited, LiveParticipantRole.Learner);
        _h.Invites.Invites[(session.SessionId, learners[1])] = (InviteStatus.Skipped, LiveParticipantRole.Learner);

        var page = (await _h.InstructorQueries().GetRosterAsync(InstructorId, session.SessionId, RosterFilter.All, 1, 50, CancellationToken.None)).Value;

        Assert.Equal("Invited", page.Items.Single(i => i.UserId == learners[0]).InviteStatus);
        Assert.Equal("Skipped", page.Items.Single(i => i.UserId == learners[1]).InviteStatus);
        Assert.Null(page.Items.Single(i => i.UserId == learners[2]).InviteStatus);
    }

    [Fact]
    public async Task Roster_AnEmptyCourse_IsAnEmptyPage_WithoutAskingForContacts()
    {
        var (session, _) = RosterFixture(learnerCount: 0);

        var page = (await _h.InstructorQueries().GetRosterAsync(InstructorId, session.SessionId, RosterFilter.All, 1, 50, CancellationToken.None)).Value;

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Empty(_h.Contacts.BatchRequests);
    }
}
