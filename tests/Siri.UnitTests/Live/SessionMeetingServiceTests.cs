using System.Reflection;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.SharedKernel;

namespace Siri.UnitTests.Live;

/// <summary>Instructor-facing room operations: ownership, validation, encryption at rest, the exact needs-action table, and "no URL in any summary".</summary>
public class SessionMeetingServiceTests
{
    private static readonly Guid InstructorId = Guid.NewGuid();
    private static readonly Guid OtherInstructorId = Guid.NewGuid();

    private readonly InMemorySessionMeetingRepository _meetings = new();
    private readonly FakeSchedule _schedule = new();
    private readonly FakeCatalog _catalog = new();
    private readonly FakeClock _clock = new(LiveTestData.Now);
    private readonly ISensitiveDataProtector _protector = LiveTestData.Protector();
    private readonly ListLogger<SessionMeetingService> _logger = new();

    private SessionMeetingService Service() => new(
        _meetings,
        _schedule,
        _catalog,
        new MeetingLinkValidator(LiveTestData.OptionsOf()),
        _protector,
        _clock,
        _logger);

    private LiveSessionContext AddSession(Guid? instructor = null, LiveSessionStatus status = LiveSessionStatus.Scheduled, DateTime? starts = null, DateTime? ends = null)
    {
        var context = LiveTestData.Context(instructorUserId: instructor ?? InstructorId, status: status, startsAtUtc: starts, endsAtUtc: ends);
        _schedule.Contexts.Add(context);
        return context;
    }

    // ---- needsAction table (docs/contracts/P11-FE-live-dto-appendix.md A.5 — exact) ------------------

    [Theory]
    [InlineData(MeetingSyncStatus.Pending, false, MeetingNeedsAction.Waiting)]
    [InlineData(MeetingSyncStatus.Pending, true, MeetingNeedsAction.Waiting)]
    [InlineData(MeetingSyncStatus.AwaitingLink, false, MeetingNeedsAction.PasteLink)]
    [InlineData(MeetingSyncStatus.NeedsReconnect, false, MeetingNeedsAction.ReconnectGoogle)]
    [InlineData(MeetingSyncStatus.NeedsReconnect, true, MeetingNeedsAction.ReconnectGoogle)]
    [InlineData(MeetingSyncStatus.Failed, false, MeetingNeedsAction.Retry)]
    [InlineData(MeetingSyncStatus.Failed, true, MeetingNeedsAction.Retry)]
    [InlineData(MeetingSyncStatus.Synced, true, MeetingNeedsAction.None)]
    [InlineData(MeetingSyncStatus.Synced, false, MeetingNeedsAction.PasteLink)]
    [InlineData(MeetingSyncStatus.PendingDelete, false, MeetingNeedsAction.None)]
    [InlineData(MeetingSyncStatus.PendingDelete, true, MeetingNeedsAction.None)]
    [InlineData(MeetingSyncStatus.Deleted, false, MeetingNeedsAction.None)]
    [InlineData(MeetingSyncStatus.Deleted, true, MeetingNeedsAction.None)]
    public void DeriveNeedsAction_FollowsTheContractTable(MeetingSyncStatus status, bool hasLink, MeetingNeedsAction expected)
    {
        Assert.Equal(expected, SessionMeetingService.DeriveNeedsAction(status, hasLink));
    }

    [Fact]
    public void DeriveNeedsAction_CoversEveryStatus()
    {
        foreach (var status in Enum.GetValues<MeetingSyncStatus>())
        {
            // No status may be left undefined (would silently fall into a default branch).
            _ = SessionMeetingService.DeriveNeedsAction(status, hasMeetingLink: true);
            _ = SessionMeetingService.DeriveNeedsAction(status, hasMeetingLink: false);
        }
    }

    [Fact]
    public void ToSummary_NeverExposesTheUrl_OnlyTheFactThatOneExists()
    {
        var meeting = SESSION_MEETING.Stage(Guid.NewGuid());
        meeting.SetManualLink(_protector.Encrypt("https://meet.google.com/very-secret-room"));

        var summary = SessionMeetingService.ToSummary(meeting);

        Assert.True(summary.HasMeetingLink);
        Assert.True(summary.IsUsable);
        Assert.Equal(MeetingNeedsAction.None, summary.NeedsAction);

        // Structural guarantee: no member of the summary is a URL/string that could carry the link (only ErrorCode is a string).
        var stringMembers = typeof(InstructorMeetingSummary).GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(p => p.PropertyType == typeof(string))
            .Select(p => p.Name)
            .ToArray();
        Assert.Equal(["ErrorCode"], stringMembers);
        Assert.DoesNotContain("very-secret-room", System.Text.Json.JsonSerializer.Serialize(summary));
    }

    // ---- Course meetings list ---------------------------------------------------------------------

    [Fact]
    public async Task GetCourseMeetingSummaries_NotTheOwner_IsNotFoundExactlyLikeAMissingCourse()
    {
        var session = AddSession();
        _meetings.Meetings.Add(SESSION_MEETING.Stage(session.SessionId));
        // OtherInstructorId owns nothing.

        var notOwner = await Service().GetCourseMeetingSummariesAsync(session.CourseId, OtherInstructorId, CancellationToken.None);
        var missing = await Service().GetCourseMeetingSummariesAsync(Guid.NewGuid(), OtherInstructorId, CancellationToken.None);

        Assert.True(notOwner.IsFailure);
        Assert.Equal("not_found", notOwner.Error.Code);
        Assert.Equal(missing.Error, notOwner.Error);
    }

    [Fact]
    public async Task GetCourseMeetingSummaries_Owner_ListsMeetingsInScheduleOrder_AndSkipsSessionsWithoutARow()
    {
        var courseId = Guid.NewGuid();
        var later = LiveTestData.Context(instructorUserId: InstructorId, startsAtUtc: LiveTestData.Now.AddDays(3), endsAtUtc: LiveTestData.Now.AddDays(3).AddHours(1)) with { CourseId = courseId };
        var earlier = LiveTestData.Context(instructorUserId: InstructorId, startsAtUtc: LiveTestData.Now.AddDays(1), endsAtUtc: LiveTestData.Now.AddDays(1).AddHours(1)) with { CourseId = courseId };
        var withoutRow = LiveTestData.Context(instructorUserId: InstructorId, startsAtUtc: LiveTestData.Now.AddDays(5), endsAtUtc: LiveTestData.Now.AddDays(5).AddHours(1)) with { CourseId = courseId };
        _schedule.Contexts.AddRange([later, earlier, withoutRow]);
        _meetings.Meetings.Add(SESSION_MEETING.Stage(later.SessionId));
        _meetings.Meetings.Add(SESSION_MEETING.Stage(earlier.SessionId));
        _catalog.Owners.Add((courseId, InstructorId));

        var result = await Service().GetCourseMeetingSummariesAsync(courseId, InstructorId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([earlier.SessionId, later.SessionId], result.Value.Items.Select(i => i.SessionId).ToArray());
        Assert.All(result.Value.Items, item => Assert.Equal(MeetingNeedsAction.Waiting, item.NeedsAction));
    }

    [Fact]
    public async Task GetCourseMeetingSummaries_CourseWithNoSessions_IsAnEmptyList()
    {
        var courseId = Guid.NewGuid();
        _catalog.Owners.Add((courseId, InstructorId));

        var result = await Service().GetCourseMeetingSummariesAsync(courseId, InstructorId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(result.Value.Items);
    }

    // ---- SetManualLink ----------------------------------------------------------------------------

    [Fact]
    public async Task SetManualLink_Owner_StoresTheLinkEncrypted_AndTheSummaryHasNoUrl()
    {
        var session = AddSession();
        _meetings.Meetings.Add(SESSION_MEETING.Stage(session.SessionId));

        var result = await Service().SetManualLinkAsync(InstructorId, session.SessionId, "https://meet.google.com/abc-defg-hij", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.HasMeetingLink);
        Assert.True(result.Value.IsUsable);
        Assert.Equal(MeetingSyncStatus.Synced, result.Value.SyncStatus);
        Assert.Equal(MeetingProvider.Manual, result.Value.Provider);
        Assert.Equal(MeetingNeedsAction.None, result.Value.NeedsAction);

        var stored = _meetings.Meetings.Single();
        Assert.NotNull(stored.MEET_URL_ENCRYPTED);
        Assert.DoesNotContain("meet.google.com", stored.MEET_URL_ENCRYPTED);
        Assert.Equal("https://meet.google.com/abc-defg-hij", _protector.Decrypt(stored.MEET_URL_ENCRYPTED!));
        Assert.Equal(InstructorId, stored.INSTRUCTOR_USER_ID);
        Assert.Equal(1, _meetings.SaveCount);
    }

    [Fact]
    public async Task SetManualLink_NoMeetingRowYet_StagesOneAndSetsTheLink()
    {
        var session = AddSession();

        var result = await Service().SetManualLinkAsync(InstructorId, session.SessionId, "https://zoom.us/j/1", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(_meetings.Meetings);
        Assert.Equal(session.SessionId, _meetings.Meetings[0].SESSION_ID);
    }

    [Fact]
    public async Task SetManualLink_NormalizesTheUrlBeforeEncrypting()
    {
        var session = AddSession();

        await Service().SetManualLinkAsync(InstructorId, session.SessionId, "  HTTPS://MEET.GOOGLE.COM/abc#frag  ", CancellationToken.None);

        Assert.Equal("https://meet.google.com/abc", _protector.Decrypt(_meetings.Meetings.Single().MEET_URL_ENCRYPTED!));
    }

    [Fact]
    public async Task SetManualLink_ReplacingAGoogleEvent_QueuesTheEventForDeletion()
    {
        var session = AddSession();
        var meeting = SESSION_MEETING.Stage(session.SessionId);
        meeting.AssignProvider(MeetingProvider.GoogleMeet, Guid.NewGuid());
        meeting.RecordGoogleSynced(MeetingProvider.GoogleMeet, "evt-1", _protector.Encrypt("https://meet.google.com/old"), Guid.NewGuid(), _clock);
        _meetings.Meetings.Add(meeting);

        var result = await Service().SetManualLinkAsync(InstructorId, session.SessionId, "https://zoom.us/j/9", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MeetingSyncStatus.PendingDelete, result.Value.SyncStatus);
        Assert.Equal(MeetingNeedsAction.None, result.Value.NeedsAction);
        Assert.Equal(MeetingProvider.Manual, result.Value.Provider);
    }

    [Fact]
    public async Task SetManualLink_SomeoneElsesSession_IsForbidden_AndNothingIsWritten()
    {
        var session = AddSession(instructor: OtherInstructorId);
        _meetings.Meetings.Add(SESSION_MEETING.Stage(session.SessionId));

        var result = await Service().SetManualLinkAsync(InstructorId, session.SessionId, "https://zoom.us/j/9", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
        Assert.Null(_meetings.Meetings.Single().MEET_URL_ENCRYPTED);
        Assert.Equal(0, _meetings.SaveCount);
    }

    [Fact]
    public async Task SetManualLink_UnknownSession_IsNotFound()
    {
        var result = await Service().SetManualLinkAsync(InstructorId, Guid.NewGuid(), "https://zoom.us/j/9", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task SetManualLink_CancelledSession_IsConflictNotEditable()
    {
        var session = AddSession(status: LiveSessionStatus.Cancelled);

        var result = await Service().SetManualLinkAsync(InstructorId, session.SessionId, "https://zoom.us/j/9", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(SessionMeetingService.SessionNotEditableReason, result.Error.Reason);
    }

    [Fact]
    public async Task SetManualLink_FinishedSession_IsConflictNotEditable()
    {
        var session = AddSession(starts: LiveTestData.Now.AddHours(-3), ends: LiveTestData.Now.AddHours(-1));

        var result = await Service().SetManualLinkAsync(InstructorId, session.SessionId, "https://zoom.us/j/9", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SessionMeetingService.SessionNotEditableReason, result.Error.Reason);
    }

    [Fact]
    public async Task SetManualLink_SessionInProgress_IsStillEditable()
    {
        var session = AddSession(starts: LiveTestData.Now.AddMinutes(-10), ends: LiveTestData.Now.AddHours(1));

        var result = await Service().SetManualLinkAsync(InstructorId, session.SessionId, "https://zoom.us/j/9", CancellationToken.None);

        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task SetManualLink_DeletedMeeting_IsConflictNotEditable()
    {
        var session = AddSession();
        var meeting = SESSION_MEETING.Stage(session.SessionId);
        meeting.MarkDeleted();
        _meetings.Meetings.Add(meeting);

        var result = await Service().SetManualLinkAsync(InstructorId, session.SessionId, "https://zoom.us/j/9", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(SessionMeetingService.SessionNotEditableReason, result.Error.Reason);
    }

    [Theory]
    [InlineData("javascript:alert(1)", MeetingLinkValidator.InvalidReason)]
    [InlineData("http://meet.google.com/abc", MeetingLinkValidator.InvalidReason)]
    [InlineData("https://evil.example.test/abc", MeetingLinkValidator.HostNotAllowedReason)]
    [InlineData("https://meet.google.com@evil.com/", MeetingLinkValidator.InvalidReason)]
    [InlineData(null, MeetingLinkValidator.InvalidReason)]
    public async Task SetManualLink_InvalidUrl_IsAValidationErrorWithAStableReason_AndNothingIsStored(string? url, string reason)
    {
        var session = AddSession();
        _meetings.Meetings.Add(SESSION_MEETING.Stage(session.SessionId));

        var result = await Service().SetManualLinkAsync(InstructorId, session.SessionId, url, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
        Assert.Equal(reason, result.Error.Reason);
        Assert.Null(_meetings.Meetings.Single().MEET_URL_ENCRYPTED);
        Assert.Equal(0, _meetings.SaveCount);
    }

    [Fact]
    public async Task SetManualLink_ConcurrentChange_IsAConflict()
    {
        var session = AddSession();
        _meetings.Meetings.Add(SESSION_MEETING.Stage(session.SessionId));
        _meetings.ThrowOnNextSave = new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException("changed");

        var result = await Service().SetManualLinkAsync(InstructorId, session.SessionId, "https://zoom.us/j/9", CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
    }

    [Fact]
    public async Task SetManualLink_Log_ContainsTheSessionAndHostButNeverTheUrl()
    {
        var session = AddSession();
        _meetings.Meetings.Add(SESSION_MEETING.Stage(session.SessionId));

        await Service().SetManualLinkAsync(InstructorId, session.SessionId, "https://zoom.us/j/777888999?pwd=TOPSECRETPASSCODE", CancellationToken.None);

        var logs = _logger.All;
        Assert.Contains(session.SessionId.ToString(), logs);
        Assert.Contains("zoom.us", logs);
        Assert.DoesNotContain("TOPSECRETPASSCODE", logs);
        Assert.DoesNotContain("777888999", logs);
        Assert.DoesNotContain("https://", logs);
    }

    // ---- Resync ----------------------------------------------------------------------------------

    [Theory]
    [InlineData(MeetingSyncStatus.Failed)]
    [InlineData(MeetingSyncStatus.AwaitingLink)]
    [InlineData(MeetingSyncStatus.NeedsReconnect)]
    public async Task Resync_StuckMeetingWithoutAUrl_GoesBackToPending(MeetingSyncStatus status)
    {
        var session = AddSession();
        var meeting = MeetingIn(session.SessionId, status);
        _meetings.Meetings.Add(meeting);

        var result = await Service().ResyncAsync(InstructorId, session.SessionId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(MeetingSyncStatus.Pending, result.Value.SyncStatus);
        Assert.Equal(MeetingNeedsAction.Waiting, result.Value.NeedsAction);
        Assert.Equal(1, _meetings.SaveCount);
    }

    [Theory]
    [InlineData(MeetingSyncStatus.Synced)]
    [InlineData(MeetingSyncStatus.PendingDelete)]
    [InlineData(MeetingSyncStatus.Deleted)]
    [InlineData(MeetingSyncStatus.Pending)]
    public async Task Resync_MeetingThatDoesNotNeedIt_IsConflictNotResyncable(MeetingSyncStatus status)
    {
        var session = AddSession();
        _meetings.Meetings.Add(MeetingIn(session.SessionId, status));

        var result = await Service().ResyncAsync(InstructorId, session.SessionId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("conflict", result.Error.Code);
        Assert.Equal(SessionMeetingService.MeetingNotResyncableReason, result.Error.Reason);
        Assert.Equal(0, _meetings.SaveCount);
    }

    [Fact]
    public async Task Resync_SomeoneElsesSession_IsForbidden()
    {
        var session = AddSession(instructor: OtherInstructorId);
        _meetings.Meetings.Add(MeetingIn(session.SessionId, MeetingSyncStatus.Failed));

        var result = await Service().ResyncAsync(InstructorId, session.SessionId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("forbidden", result.Error.Code);
        Assert.Equal(MeetingSyncStatus.Failed, _meetings.Meetings.Single().SYNC_STATUS);
    }

    [Fact]
    public async Task Resync_UnknownSession_IsNotFound()
    {
        var result = await Service().ResyncAsync(InstructorId, Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task Resync_SessionWithoutAMeetingRow_IsNotFound()
    {
        var session = AddSession();

        var result = await Service().ResyncAsync(InstructorId, session.SessionId, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    // ---- RevealUrl -------------------------------------------------------------------------------

    [Fact]
    public void RevealUrl_DecryptsTheStoredUrl()
    {
        var meeting = SESSION_MEETING.Stage(Guid.NewGuid());
        meeting.SetManualLink(_protector.Encrypt("https://zoom.us/j/55"));

        Assert.Equal("https://zoom.us/j/55", Service().RevealUrl(meeting));
    }

    [Fact]
    public void RevealUrl_NoUrl_IsNull()
    {
        Assert.Null(Service().RevealUrl(SESSION_MEETING.Stage(Guid.NewGuid())));
    }

    [Fact]
    public void RevealUrl_CiphertextEncryptedWithAnotherKey_IsNull_AndTheFailureIsLoggedWithoutTheValue()
    {
        var otherKeyProtector = LiveTestData.Protector();
        var meeting = SESSION_MEETING.Stage(Guid.NewGuid());
        meeting.SetManualLink(otherKeyProtector.Encrypt("https://zoom.us/j/TOPSECRET"));

        var url = Service().RevealUrl(meeting);

        Assert.Null(url);
        Assert.Contains(meeting.SESSION_ID.ToString(), _logger.All);
        Assert.DoesNotContain("TOPSECRET", _logger.All);
        Assert.DoesNotContain(meeting.MEET_URL_ENCRYPTED!, _logger.All);
    }

    private SESSION_MEETING MeetingIn(Guid sessionId, MeetingSyncStatus status)
    {
        var meeting = SESSION_MEETING.Stage(sessionId);
        switch (status)
        {
            case MeetingSyncStatus.Pending:
                break;
            case MeetingSyncStatus.AwaitingLink:
                meeting.ResolveAsAwaitingLink();
                break;
            case MeetingSyncStatus.NeedsReconnect:
                meeting.RecordNeedsReconnect("invalid_grant");
                break;
            case MeetingSyncStatus.Failed:
                for (var i = 0; i < SESSION_MEETING.MaxAttempts; i++)
                {
                    meeting.RecordAttemptFailed("google_transient", _clock);
                }

                break;
            case MeetingSyncStatus.Synced:
                meeting.SetManualLink(_protector.Encrypt("https://zoom.us/j/1"));
                break;
            case MeetingSyncStatus.PendingDelete:
                meeting.AssignProvider(MeetingProvider.GoogleMeet, Guid.NewGuid());
                meeting.RecordGoogleSynced(MeetingProvider.GoogleMeet, "evt", _protector.Encrypt("https://meet.google.com/x"), Guid.NewGuid(), _clock);
                meeting.MarkSessionCancelled();
                break;
            case MeetingSyncStatus.Deleted:
                meeting.MarkDeleted();
                break;
        }

        Assert.Equal(status, meeting.SYNC_STATUS);
        return meeting;
    }
}
