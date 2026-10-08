using Siri.Modules.Live.Domain;
using Siri.Modules.Live.Infrastructure;

namespace Siri.UnitTests.Live;

/// <summary>The two cross-module seams Catalog calls into: the sink (stages meeting rows, never saves) and the publish-gate readiness reader.</summary>
public class LiveMeetingSinkAndReadinessTests
{
    private readonly InMemorySessionMeetingRepository _meetings = new();
    private readonly FakeClock _clock = new(LiveTestData.Now);

    // ---- Sink -------------------------------------------------------------------------------------

    [Fact]
    public async Task Scheduled_StagesAPendingMeeting_WithoutSaving()
    {
        var sessionId = Guid.NewGuid();

        await new LiveMeetingSink(_meetings).OnSessionScheduledAsync(sessionId, CancellationToken.None);

        var meeting = Assert.Single(_meetings.Meetings);
        Assert.Equal(sessionId, meeting.SESSION_ID);
        Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS);
        Assert.Equal(0, _meetings.SaveCount); // the Catalog handler owns the transaction
    }

    [Fact]
    public async Task Scheduled_Twice_DoesNotCreateADuplicateRow()
    {
        var sessionId = Guid.NewGuid();
        var sink = new LiveMeetingSink(_meetings);

        await sink.OnSessionScheduledAsync(sessionId, CancellationToken.None);
        await sink.OnSessionScheduledAsync(sessionId, CancellationToken.None);

        Assert.Single(_meetings.Meetings);
    }

    [Fact]
    public async Task Changed_BumpsTheIcsSequence_AndSendsAGoogleEventBackToPending_WithoutSaving()
    {
        var sessionId = Guid.NewGuid();
        var meeting = SESSION_MEETING.Stage(sessionId);
        meeting.AssignProvider(MeetingProvider.GoogleMeet, Guid.NewGuid());
        meeting.RecordGoogleSynced(MeetingProvider.GoogleMeet, "evt", "enc", Guid.NewGuid(), _clock);
        _meetings.Meetings.Add(meeting);

        await new LiveMeetingSink(_meetings).OnSessionChangedAsync(sessionId, CancellationToken.None);

        Assert.Equal(1, meeting.ICS_SEQUENCE);
        Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS);
        Assert.Equal(0, _meetings.SaveCount);
    }

    [Fact]
    public async Task Changed_ForAClassScheduledBeforeLiveExisted_StagesAFreshMeeting()
    {
        var sessionId = Guid.NewGuid();

        await new LiveMeetingSink(_meetings).OnSessionChangedAsync(sessionId, CancellationToken.None);

        Assert.Equal(sessionId, Assert.Single(_meetings.Meetings).SESSION_ID);
        Assert.Equal(0, _meetings.SaveCount);
    }

    [Fact]
    public async Task Changed_ManualMeeting_KeepsItsStatusAndUrl()
    {
        var sessionId = Guid.NewGuid();
        var meeting = SESSION_MEETING.Stage(sessionId);
        meeting.SetManualLink("enc-url");
        _meetings.Meetings.Add(meeting);

        await new LiveMeetingSink(_meetings).OnSessionChangedAsync(sessionId, CancellationToken.None);

        Assert.Equal(MeetingSyncStatus.Synced, meeting.SYNC_STATUS);
        Assert.True(meeting.IsUsable);
        Assert.Equal(1, meeting.ICS_SEQUENCE);
    }

    [Fact]
    public async Task Cancelled_RoutesTheMeetingToDeletion_WithoutSaving()
    {
        var sessionId = Guid.NewGuid();
        var meeting = SESSION_MEETING.Stage(sessionId);
        _meetings.Meetings.Add(meeting);

        await new LiveMeetingSink(_meetings).OnSessionCancelledAsync(sessionId, CancellationToken.None);

        Assert.Equal(MeetingSyncStatus.Deleted, meeting.SYNC_STATUS); // nothing on Google to remove
        Assert.Equal(1, meeting.ICS_SEQUENCE);
        Assert.Equal(0, _meetings.SaveCount);
    }

    [Fact]
    public async Task Cancelled_WithAGoogleEvent_QueuesItsDeletion()
    {
        var sessionId = Guid.NewGuid();
        var meeting = SESSION_MEETING.Stage(sessionId);
        meeting.AssignProvider(MeetingProvider.GoogleMeet, Guid.NewGuid());
        meeting.RecordGoogleSynced(MeetingProvider.GoogleMeet, "evt", "enc", Guid.NewGuid(), _clock);
        _meetings.Meetings.Add(meeting);

        await new LiveMeetingSink(_meetings).OnSessionCancelledAsync(sessionId, CancellationToken.None);

        Assert.Equal(MeetingSyncStatus.PendingDelete, meeting.SYNC_STATUS);
    }

    [Fact]
    public async Task Cancelled_WithNoMeetingRow_DoesNothing()
    {
        await new LiveMeetingSink(_meetings).OnSessionCancelledAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Empty(_meetings.Meetings);
        Assert.Equal(0, _meetings.SaveCount);
    }

    // ---- Readiness reader --------------------------------------------------------------------------

    [Fact]
    public async Task Readiness_ReportsOnlySessionsWithoutAUsableRoom()
    {
        var usable = Guid.NewGuid();
        var noUrl = Guid.NewGuid();
        var deleted = Guid.NewGuid();
        var noRow = Guid.NewGuid();
        var failedButHasUrl = Guid.NewGuid();
        var pendingButHasUrl = Guid.NewGuid();

        var usableMeeting = SESSION_MEETING.Stage(usable);
        usableMeeting.SetManualLink("enc");
        var noUrlMeeting = SESSION_MEETING.Stage(noUrl);
        noUrlMeeting.ResolveAsAwaitingLink();
        var deletedMeeting = SESSION_MEETING.Stage(deleted);
        deletedMeeting.MarkDeleted();
        var failedMeeting = SESSION_MEETING.Stage(failedButHasUrl);
        failedMeeting.AssignProvider(MeetingProvider.GoogleMeet, Guid.NewGuid());
        failedMeeting.RecordGoogleSynced(MeetingProvider.GoogleMeet, "e", "enc", Guid.NewGuid(), _clock);
        failedMeeting.MarkSessionChanged();
        for (var i = 0; i < SESSION_MEETING.MaxAttempts; i++)
        {
            failedMeeting.RecordAttemptFailed("google_transient", _clock);
        }

        var pendingMeeting = SESSION_MEETING.Stage(pendingButHasUrl);
        pendingMeeting.AssignProvider(MeetingProvider.GoogleMeet, Guid.NewGuid());
        pendingMeeting.RecordGoogleSynced(MeetingProvider.GoogleMeet, "e2", "enc", Guid.NewGuid(), _clock);
        pendingMeeting.MarkSessionChanged();

        _meetings.Meetings.AddRange([usableMeeting, noUrlMeeting, deletedMeeting, failedMeeting, pendingMeeting]);

        var notReady = await new LiveMeetingReadinessReader(_meetings)
            .GetSessionsWithoutUsableMeetingAsync([usable, noUrl, deleted, noRow, failedButHasUrl, pendingButHasUrl], CancellationToken.None);

        Assert.Equal(
            new[] { noUrl, deleted, noRow }.Order().ToArray(),
            notReady.Order().ToArray());
    }

    [Fact]
    public async Task Readiness_EmptyInput_IsEmpty()
    {
        var notReady = await new LiveMeetingReadinessReader(_meetings).GetSessionsWithoutUsableMeetingAsync([], CancellationToken.None);

        Assert.Empty(notReady);
    }

    [Fact]
    public async Task Readiness_DuplicateIds_AreReportedOnce()
    {
        var id = Guid.NewGuid();

        var notReady = await new LiveMeetingReadinessReader(_meetings).GetSessionsWithoutUsableMeetingAsync([id, id], CancellationToken.None);

        Assert.Equal([id], notReady.ToArray());
    }
}
