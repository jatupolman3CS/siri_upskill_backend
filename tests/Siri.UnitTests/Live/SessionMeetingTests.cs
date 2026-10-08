using Siri.Modules.Live.Domain;

namespace Siri.UnitTests.Live;

/// <summary>Unit tests for <see cref="SESSION_MEETING"/>'s state machine (docs/contracts/
/// P11-03-live-module-google-meetings.md §2.2/§2.3).</summary>
public class SessionMeetingTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 3, 0, 0, DateTimeKind.Utc);

    private static SESSION_MEETING Staged() => SESSION_MEETING.Stage(Guid.NewGuid());

    /// <summary>A meeting whose Google event exists and room URL is known.</summary>
    private static SESSION_MEETING GoogleSynced(FakeClock? clock = null)
    {
        var meeting = Staged();
        var accountId = Guid.NewGuid();
        meeting.AssignProvider(MeetingProvider.GoogleMeet, accountId);
        meeting.RecordGoogleSynced(MeetingProvider.GoogleMeet, "evt-1", "enc-url", accountId, clock ?? new FakeClock(Now));
        return meeting;
    }

    private static SESSION_MEETING ManualSynced()
    {
        var meeting = Staged();
        meeting.SetManualLink("enc-manual-url");
        return meeting;
    }

    // ---- Stage ---------------------------------------------------------------------------------

    [Fact]
    public void Stage_NewSession_IsPendingWithUndecidedProvider()
    {
        var sessionId = Guid.NewGuid();

        var meeting = SESSION_MEETING.Stage(sessionId);

        Assert.NotEqual(Guid.Empty, meeting.SESSION_MEETING_ID);
        Assert.Equal(sessionId, meeting.SESSION_ID);
        Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS);
        Assert.Null(meeting.PROVIDER);
        Assert.Equal(0, meeting.ICS_SEQUENCE);
        Assert.Equal(0, meeting.ATTEMPTS);
        Assert.Null(meeting.MEET_URL_ENCRYPTED);
        Assert.Null(meeting.PROVIDER_EVENT_ID);
        Assert.False(meeting.IsUsable);
    }

    [Fact]
    public void Stage_EmptySessionId_Throws()
    {
        Assert.Throws<ArgumentException>(() => SESSION_MEETING.Stage(Guid.Empty));
    }

    // ---- MarkSessionChanged --------------------------------------------------------------------

    [Fact]
    public void MarkSessionChanged_GoogleEventExists_BumpsSequenceAndResetsRetryState()
    {
        var clock = new FakeClock(Now);
        var meeting = GoogleSynced(clock);
        meeting.MarkSessionChanged();
        meeting.RecordAttemptFailed("google.transient", clock); // leaves a retry time + attempt on the row
        Assert.NotNull(meeting.NEXT_RETRY_AT_UTC);

        meeting.MarkSessionChanged();

        Assert.Equal(2, meeting.ICS_SEQUENCE);
        Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS);
        Assert.Equal(0, meeting.ATTEMPTS);
        Assert.Null(meeting.NEXT_RETRY_AT_UTC);
    }

    [Fact]
    public void MarkSessionChanged_SyncedGoogleEvent_GoesBackToPending()
    {
        var meeting = GoogleSynced();

        meeting.MarkSessionChanged();

        Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS);
        Assert.Equal(1, meeting.ICS_SEQUENCE);
        Assert.True(meeting.IsUsable); // old room keeps working while the event is patched
    }

    [Fact]
    public void MarkSessionChanged_ManualMeeting_OnlyBumpsSequence()
    {
        var meeting = ManualSynced();

        meeting.MarkSessionChanged();

        Assert.Equal(MeetingSyncStatus.Synced, meeting.SYNC_STATUS);
        Assert.Equal(1, meeting.ICS_SEQUENCE);
    }

    [Fact]
    public void MarkSessionChanged_StagedMeetingWithoutProvider_StaysPending()
    {
        var meeting = Staged();

        meeting.MarkSessionChanged();

        Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS);
        Assert.Null(meeting.PROVIDER);
        Assert.Equal(1, meeting.ICS_SEQUENCE);
    }

    [Fact]
    public void MarkSessionChanged_PendingDelete_IsNotResurrected()
    {
        var meeting = GoogleSynced();
        meeting.MarkSessionCancelled();

        meeting.MarkSessionChanged();

        Assert.Equal(MeetingSyncStatus.PendingDelete, meeting.SYNC_STATUS);
        Assert.Equal(2, meeting.ICS_SEQUENCE);
    }

    // ---- MarkSessionCancelled ------------------------------------------------------------------

    [Fact]
    public void MarkSessionCancelled_WithGoogleEvent_GoesToPendingDelete()
    {
        var meeting = GoogleSynced();

        meeting.MarkSessionCancelled();

        Assert.Equal(MeetingSyncStatus.PendingDelete, meeting.SYNC_STATUS);
        Assert.Equal(1, meeting.ICS_SEQUENCE);
        Assert.Equal(0, meeting.ATTEMPTS);
        Assert.Null(meeting.NEXT_RETRY_AT_UTC);
    }

    [Fact]
    public void MarkSessionCancelled_WithoutEvent_IsDeleted()
    {
        var meeting = ManualSynced();

        meeting.MarkSessionCancelled();

        Assert.Equal(MeetingSyncStatus.Deleted, meeting.SYNC_STATUS);
        Assert.Equal(1, meeting.ICS_SEQUENCE);
        Assert.False(meeting.IsUsable);
    }

    [Fact]
    public void MarkSessionCancelled_CalledTwice_BumpsSequenceOnce()
    {
        var meeting = Staged();

        meeting.MarkSessionCancelled();
        meeting.MarkSessionCancelled();

        Assert.Equal(MeetingSyncStatus.Deleted, meeting.SYNC_STATUS);
        Assert.Equal(1, meeting.ICS_SEQUENCE);
    }

    // ---- SetManualLink -------------------------------------------------------------------------

    [Fact]
    public void SetManualLink_NoGoogleEvent_SyncsImmediately()
    {
        var meeting = Staged();

        meeting.SetManualLink("enc-url");

        Assert.Equal(MeetingProvider.Manual, meeting.PROVIDER);
        Assert.Equal("enc-url", meeting.MEET_URL_ENCRYPTED);
        Assert.Equal(MeetingSyncStatus.Synced, meeting.SYNC_STATUS);
        Assert.Null(meeting.ERROR);
        Assert.True(meeting.IsUsable);
    }

    [Fact]
    public void SetManualLink_GoogleEventExists_QueuesDeletionOfTheEvent()
    {
        var meeting = GoogleSynced();

        meeting.SetManualLink("enc-manual");

        Assert.Equal(MeetingProvider.Manual, meeting.PROVIDER);
        Assert.Equal("enc-manual", meeting.MEET_URL_ENCRYPTED);
        Assert.Equal(MeetingSyncStatus.PendingDelete, meeting.SYNC_STATUS);
        Assert.Equal("evt-1", meeting.PROVIDER_EVENT_ID);
        Assert.True(meeting.IsUsable);
    }

    [Fact]
    public void SetManualLink_ClearsErrorAndRetryState()
    {
        var clock = new FakeClock(Now);
        var meeting = Staged();
        meeting.RecordAttemptFailed("google.transient", clock);

        meeting.SetManualLink("enc-url");

        Assert.Null(meeting.ERROR);
        Assert.Equal(0, meeting.ATTEMPTS);
        Assert.Null(meeting.NEXT_RETRY_AT_UTC);
    }

    [Fact]
    public void SetManualLink_DeletedMeeting_Throws()
    {
        var meeting = Staged();
        meeting.MarkSessionCancelled();

        Assert.Throws<InvalidOperationException>(() => meeting.SetManualLink("enc-url"));
        Assert.Null(meeting.MEET_URL_ENCRYPTED);
        Assert.Equal(MeetingSyncStatus.Deleted, meeting.SYNC_STATUS);
    }

    [Fact]
    public void SetManualLink_BlankUrl_Throws()
    {
        var meeting = Staged();

        Assert.Throws<ArgumentException>(() => meeting.SetManualLink(" "));
    }

    // ---- AssignProvider ------------------------------------------------------------------------

    [Fact]
    public void AssignProvider_GoogleMeet_StoresProviderAndAccount()
    {
        var meeting = Staged();
        var accountId = Guid.NewGuid();

        meeting.AssignProvider(MeetingProvider.GoogleMeet, accountId);

        Assert.Equal(MeetingProvider.GoogleMeet, meeting.PROVIDER);
        Assert.Equal(accountId, meeting.INSTRUCTOR_GOOGLE_ACCOUNT_ID);
        Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS);
    }

    [Fact]
    public void AssignProvider_GoogleMeetWithoutAccount_Throws()
    {
        var meeting = Staged();

        Assert.Throws<ArgumentException>(() => meeting.AssignProvider(MeetingProvider.GoogleMeet, null));
        Assert.Throws<ArgumentException>(() => meeting.AssignProvider(MeetingProvider.GoogleMeet, Guid.Empty));
        Assert.Null(meeting.PROVIDER);
    }

    [Fact]
    public void AssignProvider_Logging_DoesNotStoreAnAccount()
    {
        var meeting = Staged();

        meeting.AssignProvider(MeetingProvider.Logging, Guid.NewGuid());

        Assert.Equal(MeetingProvider.Logging, meeting.PROVIDER);
        Assert.Null(meeting.INSTRUCTOR_GOOGLE_ACCOUNT_ID);
    }

    // ---- RecordGoogleSynced --------------------------------------------------------------------

    [Fact]
    public void RecordGoogleSynced_StoresEventUrlAndResetsRetryState()
    {
        var clock = new FakeClock(Now);
        var meeting = Staged();
        var accountId = Guid.NewGuid();
        meeting.AssignProvider(MeetingProvider.GoogleMeet, accountId);
        meeting.RecordAttemptFailed("conference_pending", clock);

        meeting.RecordGoogleSynced(MeetingProvider.GoogleMeet, "evt-9", "enc-url", accountId, clock);

        Assert.Equal(MeetingProvider.GoogleMeet, meeting.PROVIDER);
        Assert.Equal("evt-9", meeting.PROVIDER_EVENT_ID);
        Assert.Equal("enc-url", meeting.MEET_URL_ENCRYPTED);
        Assert.Equal(accountId, meeting.INSTRUCTOR_GOOGLE_ACCOUNT_ID);
        Assert.Equal(MeetingSyncStatus.Synced, meeting.SYNC_STATUS);
        Assert.Equal(0, meeting.ATTEMPTS);
        Assert.Null(meeting.NEXT_RETRY_AT_UTC);
        Assert.Equal(Now, meeting.LAST_SYNC_AT_UTC);
        Assert.Null(meeting.ERROR);
        Assert.True(meeting.IsUsable);
    }

    [Fact]
    public void RecordGoogleSynced_LoggingProvider_HasNoAccount()
    {
        var meeting = Staged();

        meeting.RecordGoogleSynced(MeetingProvider.Logging, "dev-evt", "enc-url", Guid.NewGuid(), new FakeClock(Now));

        Assert.Equal(MeetingProvider.Logging, meeting.PROVIDER);
        Assert.Null(meeting.INSTRUCTOR_GOOGLE_ACCOUNT_ID);
    }

    [Fact]
    public void RecordGoogleSynced_ManualProvider_Throws()
    {
        var meeting = Staged();

        Assert.Throws<ArgumentException>(() =>
            meeting.RecordGoogleSynced(MeetingProvider.Manual, "evt", "enc-url", null, new FakeClock(Now)));
    }

    [Fact]
    public void RecordGoogleSynced_BlankEventOrUrl_Throws()
    {
        var meeting = Staged();
        var clock = new FakeClock(Now);

        Assert.Throws<ArgumentException>(() => meeting.RecordGoogleSynced(MeetingProvider.GoogleMeet, " ", "enc-url", Guid.NewGuid(), clock));
        Assert.Throws<ArgumentException>(() => meeting.RecordGoogleSynced(MeetingProvider.GoogleMeet, "evt", "", Guid.NewGuid(), clock));
        Assert.Throws<ArgumentException>(() =>
            meeting.RecordGoogleSynced(MeetingProvider.GoogleMeet, new string('e', 201), "enc-url", Guid.NewGuid(), clock));
        Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS);
    }

    // ---- ResolveAsAwaitingLink / RecordNeedsReconnect ------------------------------------------

    [Fact]
    public void ResolveAsAwaitingLink_SetsManualProviderAndWaitsForLink()
    {
        var meeting = Staged();

        meeting.ResolveAsAwaitingLink();

        Assert.Equal(MeetingProvider.Manual, meeting.PROVIDER);
        Assert.Equal(MeetingSyncStatus.AwaitingLink, meeting.SYNC_STATUS);
        Assert.False(meeting.IsUsable);
    }

    [Fact]
    public void RecordNeedsReconnect_KeepsExistingUrlUsable()
    {
        var meeting = GoogleSynced();

        meeting.RecordNeedsReconnect("invalid_grant");

        Assert.Equal(MeetingSyncStatus.NeedsReconnect, meeting.SYNC_STATUS);
        Assert.Equal("invalid_grant", meeting.ERROR);
        Assert.True(meeting.IsUsable);
    }

    [Fact]
    public void RecordNeedsReconnect_WithoutUrl_IsNotUsable()
    {
        var meeting = Staged();

        meeting.RecordNeedsReconnect("google_account_unavailable");

        Assert.Equal(MeetingSyncStatus.NeedsReconnect, meeting.SYNC_STATUS);
        Assert.False(meeting.IsUsable);
    }

    // ---- RecordAttemptFailed -------------------------------------------------------------------

    [Fact]
    public void RecordAttemptFailed_FirstFourFailures_BackOff1_5_15_60Minutes()
    {
        var clock = new FakeClock(Now);
        var meeting = Staged();
        var expectedMinutes = new[] { 1, 5, 15, 60 };

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            meeting.RecordAttemptFailed("google.transient", clock);

            Assert.Equal(attempt, meeting.ATTEMPTS);
            Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS);
            Assert.Equal(Now.AddMinutes(expectedMinutes[attempt - 1]), meeting.NEXT_RETRY_AT_UTC);
            Assert.Equal("google.transient", meeting.ERROR);
        }
    }

    [Fact]
    public void RecordAttemptFailed_FifthFailure_BecomesFailedWithoutRetry()
    {
        var clock = new FakeClock(Now);
        var meeting = Staged();

        for (var attempt = 1; attempt <= SESSION_MEETING.MaxAttempts; attempt++)
        {
            meeting.RecordAttemptFailed("google.bad_request", clock);
        }

        Assert.Equal(5, meeting.ATTEMPTS);
        Assert.Equal(MeetingSyncStatus.Failed, meeting.SYNC_STATUS);
        Assert.Null(meeting.NEXT_RETRY_AT_UTC);
    }

    [Fact]
    public void RecordAttemptFailed_PendingDelete_KeepsPendingDeleteWhileRetrying()
    {
        var clock = new FakeClock(Now);
        var meeting = GoogleSynced(clock);
        meeting.MarkSessionCancelled();

        meeting.RecordAttemptFailed("google.transient", clock);

        Assert.Equal(MeetingSyncStatus.PendingDelete, meeting.SYNC_STATUS);
        Assert.Equal(Now.AddMinutes(1), meeting.NEXT_RETRY_AT_UTC);
    }

    [Fact]
    public void RecordAttemptFailed_FromSettledState_Throws()
    {
        var meeting = ManualSynced();

        Assert.Throws<InvalidOperationException>(() => meeting.RecordAttemptFailed("google.transient", new FakeClock(Now)));
    }

    [Fact]
    public void RecordAttemptFailed_OverlongCode_IsTruncatedToTheColumn()
    {
        var meeting = Staged();

        meeting.RecordAttemptFailed(new string('x', 800), new FakeClock(Now));

        Assert.Equal(500, meeting.ERROR!.Length);
    }

    // ---- RequestResync -------------------------------------------------------------------------

    [Fact]
    public void RequestResync_FailedMeeting_ResetsToPending()
    {
        var clock = new FakeClock(Now);
        var meeting = Staged();
        meeting.AssignProvider(MeetingProvider.GoogleMeet, Guid.NewGuid());
        for (var i = 0; i < SESSION_MEETING.MaxAttempts; i++)
        {
            meeting.RecordAttemptFailed("google.transient", clock);
        }

        Assert.True(meeting.CanRequestResync);
        meeting.RequestResync();

        Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS);
        Assert.Null(meeting.PROVIDER);
        Assert.Equal(0, meeting.ATTEMPTS);
        Assert.Null(meeting.NEXT_RETRY_AT_UTC);
    }

    [Fact]
    public void RequestResync_AwaitingLinkWithoutUrl_ResetsToPending()
    {
        var meeting = Staged();
        meeting.ResolveAsAwaitingLink();

        meeting.RequestResync();

        Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS);
        Assert.Null(meeting.PROVIDER);
    }

    [Fact]
    public void RequestResync_NeedsReconnectWithoutUrl_ResetsToPending()
    {
        var meeting = Staged();
        meeting.RecordNeedsReconnect("invalid_grant");

        meeting.RequestResync();

        Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS);
    }

    [Fact]
    public void RequestResync_NeedsReconnectThatStillHasAUrl_Throws()
    {
        var meeting = GoogleSynced();
        meeting.RecordNeedsReconnect("invalid_grant");

        Assert.False(meeting.CanRequestResync);
        Assert.Throws<InvalidOperationException>(() => meeting.RequestResync());
    }

    [Fact]
    public void RequestResync_FailedMeetingThatStillHasAUrl_IsAllowed()
    {
        var clock = new FakeClock(Now);
        var meeting = GoogleSynced(clock);
        meeting.MarkSessionChanged(); // Synced -> Pending, URL kept
        for (var i = 0; i < SESSION_MEETING.MaxAttempts; i++)
        {
            meeting.RecordAttemptFailed("google.bad_request", clock);
        }

        Assert.Equal(MeetingSyncStatus.Failed, meeting.SYNC_STATUS);
        Assert.True(meeting.CanRequestResync);
        meeting.RequestResync();

        Assert.Equal(MeetingSyncStatus.Pending, meeting.SYNC_STATUS);
    }

    [Theory]
    [InlineData(MeetingSyncStatus.Synced)]
    [InlineData(MeetingSyncStatus.PendingDelete)]
    [InlineData(MeetingSyncStatus.Deleted)]
    [InlineData(MeetingSyncStatus.Pending)]
    public void RequestResync_NonResyncableStatus_Throws(MeetingSyncStatus status)
    {
        var meeting = InStatus(status);

        Assert.False(meeting.CanRequestResync);
        Assert.Throws<InvalidOperationException>(() => meeting.RequestResync());
        Assert.Equal(status, meeting.SYNC_STATUS);
    }

    // ---- FinishDelete / ResolveEndedSession / MarkDeleted --------------------------------------

    [Fact]
    public void FinishDelete_ManualUrlSurvivesAndSessionStillScheduled_BecomesSynced()
    {
        var meeting = GoogleSynced();
        meeting.SetManualLink("enc-manual"); // -> PendingDelete

        meeting.FinishDelete(sessionStillScheduled: true);

        Assert.Null(meeting.PROVIDER_EVENT_ID);
        Assert.Equal(MeetingSyncStatus.Synced, meeting.SYNC_STATUS);
        Assert.True(meeting.IsUsable);
    }

    [Fact]
    public void FinishDelete_SessionCancelled_BecomesDeleted()
    {
        var meeting = GoogleSynced();
        meeting.MarkSessionCancelled();

        meeting.FinishDelete(sessionStillScheduled: false);

        Assert.Null(meeting.PROVIDER_EVENT_ID);
        Assert.Equal(MeetingSyncStatus.Deleted, meeting.SYNC_STATUS);
        Assert.False(meeting.IsUsable);
    }

    [Fact]
    public void FinishDelete_ManualUrlButSessionNoLongerScheduled_BecomesDeleted()
    {
        var meeting = GoogleSynced();
        meeting.SetManualLink("enc-manual");

        meeting.FinishDelete(sessionStillScheduled: false);

        Assert.Equal(MeetingSyncStatus.Deleted, meeting.SYNC_STATUS);
    }

    [Fact]
    public void ResolveEndedSession_WithUrl_StaysSyncedElseDeleted()
    {
        var withUrl = ManualSynced();
        withUrl.MarkSessionChanged();
        withUrl.ResolveEndedSession();
        Assert.Equal(MeetingSyncStatus.Synced, withUrl.SYNC_STATUS);

        var withoutUrl = Staged();
        withoutUrl.ResolveEndedSession();
        Assert.Equal(MeetingSyncStatus.Deleted, withoutUrl.SYNC_STATUS);
    }

    [Fact]
    public void MarkDeleted_FromAnyState_EndsInDeleted()
    {
        var meeting = GoogleSynced();

        meeting.MarkDeleted();

        Assert.Equal(MeetingSyncStatus.Deleted, meeting.SYNC_STATUS);
        Assert.False(meeting.IsUsable);
    }

    // ---- Small job helpers ---------------------------------------------------------------------

    [Fact]
    public void AssignInstructor_StoresIdAndRejectsEmpty()
    {
        var meeting = Staged();
        var instructorId = Guid.NewGuid();

        meeting.AssignInstructor(instructorId);

        Assert.Equal(instructorId, meeting.INSTRUCTOR_USER_ID);
        Assert.Throws<ArgumentException>(() => meeting.AssignInstructor(Guid.Empty));
    }

    [Fact]
    public void ClearProviderEvent_ForgetsTheEventId()
    {
        var meeting = GoogleSynced();

        meeting.ClearProviderEvent();

        Assert.Null(meeting.PROVIDER_EVENT_ID);
    }

    [Fact]
    public void RecordError_StoresCodeWithoutChangingState()
    {
        var meeting = ManualSynced();

        meeting.RecordError("orphan_event");

        Assert.Equal("orphan_event", meeting.ERROR);
        Assert.Equal(MeetingSyncStatus.Synced, meeting.SYNC_STATUS);
    }

    [Fact]
    public void MarkAlertSent_StampsOnlyTheFirstTime()
    {
        var clock = new FakeClock(Now);
        var meeting = Staged();

        meeting.MarkAlertSent(clock);
        clock.UtcNow = Now.AddHours(5);
        meeting.MarkAlertSent(clock);

        Assert.Equal(Now, meeting.MEETING_ALERT_SENT_AT_UTC);
    }

    // ---- IsUsable ------------------------------------------------------------------------------

    [Theory]
    [InlineData(MeetingSyncStatus.Synced, true)]
    [InlineData(MeetingSyncStatus.Pending, true)]
    [InlineData(MeetingSyncStatus.NeedsReconnect, true)]
    [InlineData(MeetingSyncStatus.Failed, true)]
    [InlineData(MeetingSyncStatus.PendingDelete, true)]
    [InlineData(MeetingSyncStatus.Deleted, false)]
    public void IsUsable_WithUrl_DependsOnlyOnNotBeingDeleted(MeetingSyncStatus status, bool expected)
    {
        var meeting = InStatus(status, withUrl: true);

        Assert.Equal(status, meeting.SYNC_STATUS);
        Assert.Equal(expected, meeting.IsUsable);
    }

    [Theory]
    [InlineData(MeetingSyncStatus.Pending)]
    [InlineData(MeetingSyncStatus.AwaitingLink)]
    [InlineData(MeetingSyncStatus.NeedsReconnect)]
    [InlineData(MeetingSyncStatus.Failed)]
    public void IsUsable_WithoutUrl_IsAlwaysFalse(MeetingSyncStatus status)
    {
        var meeting = InStatus(status, withUrl: false);

        Assert.Equal(status, meeting.SYNC_STATUS);
        Assert.False(meeting.IsUsable);
    }

    /// <summary>Drives a meeting into <paramref name="status"/> through the public state machine only.</summary>
    private static SESSION_MEETING InStatus(MeetingSyncStatus status, bool withUrl = false)
    {
        var clock = new FakeClock(Now);
        var meeting = Staged();

        switch (status)
        {
            case MeetingSyncStatus.Pending:
                if (withUrl)
                {
                    meeting = GoogleSynced(clock);
                    meeting.MarkSessionChanged();
                }

                break;

            case MeetingSyncStatus.AwaitingLink:
                meeting.ResolveAsAwaitingLink();
                break;

            case MeetingSyncStatus.Synced:
                // A Synced meeting always holds a URL — there is no public way to build one without it.
                meeting.SetManualLink("enc-url");
                break;

            case MeetingSyncStatus.NeedsReconnect:
                if (withUrl)
                {
                    meeting = GoogleSynced(clock);
                }

                meeting.RecordNeedsReconnect("invalid_grant");
                break;

            case MeetingSyncStatus.Failed:
                if (withUrl)
                {
                    meeting = GoogleSynced(clock);
                    meeting.MarkSessionChanged();
                }

                for (var i = 0; i < SESSION_MEETING.MaxAttempts; i++)
                {
                    meeting.RecordAttemptFailed("google.bad_request", clock);
                }

                break;

            case MeetingSyncStatus.PendingDelete:
                meeting = GoogleSynced(clock);
                meeting.MarkSessionCancelled();
                break;

            case MeetingSyncStatus.Deleted:
                if (withUrl)
                {
                    meeting.SetManualLink("enc-url");
                }

                meeting.MarkDeleted();
                break;
        }

        return meeting;
    }
}
