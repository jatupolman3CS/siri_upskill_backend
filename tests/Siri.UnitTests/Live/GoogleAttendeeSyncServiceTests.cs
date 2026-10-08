using Microsoft.Extensions.Options;
using Siri.Integrations.Google;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.SharedKernel;

namespace Siri.UnitTests.Live;

/// <summary>
/// The opt-in Google attendee sync (docs/contracts/P11-04-live-invites-ics-reminders.md §6): who is written to the event, the cap, removal of
/// withdrawn learners, the "one Google call per session, only on a difference" rule, and the failure handling.
/// </summary>
public class GoogleAttendeeSyncServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private sealed class FakeCalendar : ICalendarProvider
    {
        public List<(string Token, string EventId, IReadOnlyList<string> Emails)> SetCalls { get; } = [];

        public Result NextResult { get; set; } = Result.Success();

        public Task<Result> SetAttendeesAsync(string accessToken, string eventId, IReadOnlyCollection<string> attendeeEmails, CancellationToken ct)
        {
            SetCalls.Add((accessToken, eventId, attendeeEmails.ToList()));
            return Task.FromResult(NextResult);
        }

        public Task<Result<CalendarEventResult>> CreateEventWithMeetAsync(string accessToken, CalendarEventRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Result<CalendarEventResult?>> FindEventByPrivateSessionIdAsync(string accessToken, string privateSessionId, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Result<CalendarEventResult>> GetEventAsync(string accessToken, string eventId, CancellationToken ct) => throw new NotSupportedException();

        public Task<Result<CalendarEventResult>> UpdateEventAsync(string accessToken, string eventId, CalendarEventRequest request, CancellationToken ct) =>
            throw new NotSupportedException();

        public Task<Result> DeleteEventAsync(string accessToken, string eventId, CancellationToken ct) => throw new NotSupportedException();
    }

    private sealed class Rig
    {
        public Rig(Action<LiveOptions>? configure = null)
        {
            Harness = new InviteHarness();
            Configure = configure;
            Accounts = new InMemoryAccountRepository();
            Oauth = new FakeGoogleOAuth();
            Alerts = new RecordingAlertSender();
            AccountLogger = new ListLogger<InstructorGoogleAccountService>();
        }

        public InviteHarness Harness { get; }

        public Action<LiveOptions>? Configure { get; }

        public InMemoryAccountRepository Accounts { get; }

        public FakeGoogleOAuth Oauth { get; }

        public RecordingAlertSender Alerts { get; }

        public ListLogger<InstructorGoogleAccountService> AccountLogger { get; }

        public FakeCalendar Calendar { get; } = new();

        /// <summary>One key for the whole rig — the refresh token stored by <see cref="ConnectInstructor"/> must decrypt in the service.</summary>
        public ISensitiveDataProtector Protector { get; } = LiveTestData.Protector();

        public ListLogger<GoogleAttendeeSyncService> Logger { get; } = new();

        public LiveProviderMode Mode { get; set; } = LiveProviderMode.GoogleMeet;

        public void ConnectInstructor()
        {
            var protector = Protector;
            Accounts.Accounts.Add(INSTRUCTOR_GOOGLE_ACCOUNT.Connect(
                Harness.InstructorUserId, "sub-1", "teacher@gmail.test", protector.Encrypt("refresh-token-1"), GoogleScopes.CalendarEventsOwned, Harness.Clock));
        }

        public GoogleAttendeeSyncService Service()
        {
            void Apply(LiveOptions o)
            {
                o.Provider = Mode;
                Configure?.Invoke(o);
            }

            var accountService = new InstructorGoogleAccountService(
                Accounts,
                Harness.Db,
                Oauth,
                new FakeStateStore(),
                Protector,
                Harness.Schedule,
                Alerts,
                Harness.Clock,
                LiveTestData.OptionsOf(Apply),
                Microsoft.Extensions.Options.Options.Create(new GoogleOAuthOptions()),
                AccountLogger);

            return new GoogleAttendeeSyncService(
                Harness.Db,
                Harness.Db,
                Harness.Schedule,
                Harness.Contacts,
                accountService,
                Calendar,
                Harness.Db,
                LiveTestData.OptionsOf(Apply),
                Harness.Clock,
                Logger);
        }

        /// <summary>A session of an opted-in course whose room is a synced Google event.</summary>
        public LiveSessionContext AddGoogleSession(Guid courseId, TimeSpan startsIn, bool optedIn = true)
        {
            var session = Harness.AddSession(courseId, startsIn);
            session = Harness.UpdateSession(session.SessionId, s => s with { GoogleAttendeeSyncEnabled = optedIn });
            Harness.AddGoogleMeeting(session.SessionId);
            return session;
        }

        /// <summary>An invited learner (invite sent <paramref name="invitedAgo"/> ago) with a reachable address.</summary>
        public (Guid UserId, string Email) AddInvitedLearner(Guid courseId, Guid sessionId, TimeSpan? invitedAgo = null)
        {
            var learner = Harness.AddLearner(courseId);
            Harness.SeedInvited(sessionId, learner.UserId, LiveParticipantRole.Learner, invitedAgo ?? TimeSpan.FromHours(1));
            return learner;
        }
    }

    // ---- Who is written to the event -------------------------------------------------------------------

    [Fact]
    public async Task Sync_WritesTheInvitedLearnersToTheGoogleEvent_InInviteOrder_AndStampsThem()
    {
        var rig = new Rig();
        rig.ConnectInstructor();
        var courseId = Guid.NewGuid();
        var session = rig.AddGoogleSession(courseId, TimeSpan.FromDays(2));
        var first = rig.AddInvitedLearner(courseId, session.SessionId, TimeSpan.FromHours(5));
        var second = rig.AddInvitedLearner(courseId, session.SessionId, TimeSpan.FromHours(3));
        var third = rig.AddInvitedLearner(courseId, session.SessionId, TimeSpan.FromHours(1));
        // the instructor's own invite is never a guest of their own event
        rig.Harness.SeedInvited(session.SessionId, rig.Harness.InstructorUserId, LiveParticipantRole.Instructor, TimeSpan.FromHours(6));

        var result = await rig.Service().SyncAsync(Ct);

        var call = Assert.Single(rig.Calendar.SetCalls);
        Assert.Equal("event-1", call.EventId);
        Assert.Equal("access-token-2", call.Token);
        Assert.Equal([first.Email, second.Email, third.Email], call.Emails);
        Assert.All([first, second, third], l => Assert.NotNull(rig.Harness.Db.InviteFor(session.SessionId, l.UserId)!.GOOGLE_ATTENDEE_SYNCED_AT_UTC));
        Assert.Null(rig.Harness.Db.InviteFor(session.SessionId, rig.Harness.InstructorUserId)!.GOOGLE_ATTENDEE_SYNCED_AT_UTC);
        Assert.Equal(1, result.SessionsSynced);
        Assert.Equal(0, result.SessionsFailed);
    }

    [Fact]
    public async Task Sync_RunTwice_CallsGoogleOnlyOnce_BecauseTheSecondRunFindsNoDifference()
    {
        var rig = new Rig();
        rig.ConnectInstructor();
        var courseId = Guid.NewGuid();
        var session = rig.AddGoogleSession(courseId, TimeSpan.FromDays(2));
        rig.AddInvitedLearner(courseId, session.SessionId);

        await rig.Service().SyncAsync(Ct);
        var second = await rig.Service().SyncAsync(Ct);
        var third = await rig.Service().SyncAsync(Ct);

        Assert.Single(rig.Calendar.SetCalls);
        Assert.Equal(new AttendeeSyncResult(0, 0, 0, 0), second);
        Assert.Equal(new AttendeeSyncResult(0, 0, 0, 0), third);
    }

    [Fact]
    public async Task Sync_ALearnerInvitedLater_IsAddedAlongsideTheExistingGuests_InOneCall()
    {
        var rig = new Rig();
        rig.ConnectInstructor();
        var courseId = Guid.NewGuid();
        var session = rig.AddGoogleSession(courseId, TimeSpan.FromDays(2));
        var early = rig.AddInvitedLearner(courseId, session.SessionId, TimeSpan.FromHours(5));
        await rig.Service().SyncAsync(Ct);

        var late = rig.AddInvitedLearner(courseId, session.SessionId, TimeSpan.FromMinutes(1));
        await rig.Service().SyncAsync(Ct);

        Assert.Equal(2, rig.Calendar.SetCalls.Count);
        Assert.Equal([early.Email, late.Email], rig.Calendar.SetCalls[1].Emails); // the whole list is set, not just the newcomer
    }

    [Fact]
    public async Task Sync_WithdrawnLearners_AreRemovedFromTheEvent_AndTheirStampIsCleared()
    {
        var rig = new Rig();
        rig.ConnectInstructor();
        var courseId = Guid.NewGuid();
        var session = rig.AddGoogleSession(courseId, TimeSpan.FromDays(2));
        var stays = rig.AddInvitedLearner(courseId, session.SessionId, TimeSpan.FromHours(5));
        var leaves = rig.AddInvitedLearner(courseId, session.SessionId, TimeSpan.FromHours(3));
        await rig.Service().SyncAsync(Ct);
        Assert.Equal(2, rig.Calendar.SetCalls[0].Emails.Count);

        // the reconcile withdrew one invite (enrollment revoked) — the stamp stays until the sync removes them from the event
        rig.Harness.ChangeInvite(session.SessionId, leaves.UserId, i => i.MarkCancelled(1, rig.Harness.Clock));
        var result = await rig.Service().SyncAsync(Ct);

        Assert.Equal(2, rig.Calendar.SetCalls.Count);
        Assert.Equal([stays.Email], rig.Calendar.SetCalls[1].Emails);
        Assert.Null(rig.Harness.Db.InviteFor(session.SessionId, leaves.UserId)!.GOOGLE_ATTENDEE_SYNCED_AT_UTC);
        Assert.NotNull(rig.Harness.Db.InviteFor(session.SessionId, stays.UserId)!.GOOGLE_ATTENDEE_SYNCED_AT_UTC);
        Assert.Equal(1, result.SessionsSynced);

        await rig.Service().SyncAsync(Ct);
        Assert.Equal(2, rig.Calendar.SetCalls.Count); // and it is done
    }

    [Fact]
    public async Task Sync_AnInvitedLearnerWhoseAddressIsGone_IsSkipped_NotWrittenToTheEvent()
    {
        var rig = new Rig();
        rig.ConnectInstructor();
        var courseId = Guid.NewGuid();
        var session = rig.AddGoogleSession(courseId, TimeSpan.FromDays(2));
        var reachable = rig.AddInvitedLearner(courseId, session.SessionId);
        var gone = rig.AddInvitedLearner(courseId, session.SessionId);
        rig.Harness.Contacts.Users.Remove(gone.UserId);

        await rig.Service().SyncAsync(Ct);

        Assert.Equal([reachable.Email], Assert.Single(rig.Calendar.SetCalls).Emails);
        Assert.Equal(InviteStatus.Skipped, rig.Harness.Db.InviteFor(session.SessionId, gone.UserId)!.STATUS);
        Assert.Null(rig.Harness.Db.InviteFor(session.SessionId, gone.UserId)!.GOOGLE_ATTENDEE_SYNCED_AT_UTC);
    }

    // ---- Which sessions qualify --------------------------------------------------------------------------

    [Fact]
    public async Task Sync_ACourseThatDidNotOptIn_IsNeverSentToGoogle()
    {
        var rig = new Rig();
        rig.ConnectInstructor();
        var courseId = Guid.NewGuid();
        var session = rig.AddGoogleSession(courseId, TimeSpan.FromDays(2), optedIn: false);
        rig.AddInvitedLearner(courseId, session.SessionId);

        var result = await rig.Service().SyncAsync(Ct);

        Assert.Empty(rig.Calendar.SetCalls);
        Assert.Equal(new AttendeeSyncResult(0, 0, 0, 0), result);
        Assert.Null(rig.Harness.Db.Invites.Single(i => i.ROLE == LiveParticipantRole.Learner).GOOGLE_ATTENDEE_SYNCED_AT_UTC);
    }

    [Fact]
    public async Task Sync_OnlyForGoogleMeetRoomsThatAreSyncedAndHaveAnEvent()
    {
        var rig = new Rig();
        rig.ConnectInstructor();
        var courseId = Guid.NewGuid();

        var manual = rig.Harness.AddSession(courseId, TimeSpan.FromDays(2));
        rig.Harness.UpdateSession(manual.SessionId, s => s with { GoogleAttendeeSyncEnabled = true });
        rig.Harness.AddManualMeeting(manual.SessionId);
        rig.AddInvitedLearner(courseId, manual.SessionId);

        var reconnect = rig.AddGoogleSession(courseId, TimeSpan.FromDays(3));
        rig.Harness.ChangeMeeting(reconnect.SessionId, m => m.RecordNeedsReconnect("invalid_grant"));
        rig.AddInvitedLearner(courseId, reconnect.SessionId);

        var noMeeting = rig.Harness.AddSession(courseId, TimeSpan.FromDays(4));
        rig.Harness.UpdateSession(noMeeting.SessionId, s => s with { GoogleAttendeeSyncEnabled = true });
        rig.AddInvitedLearner(courseId, noMeeting.SessionId);

        await rig.Service().SyncAsync(Ct);

        Assert.Empty(rig.Calendar.SetCalls);
    }

    [Fact]
    public async Task Sync_NotForASessionThatHasStartedOrBeenCancelled()
    {
        var rig = new Rig();
        rig.ConnectInstructor();
        var courseId = Guid.NewGuid();
        var started = rig.AddGoogleSession(courseId, TimeSpan.FromMinutes(-5));
        rig.AddInvitedLearner(courseId, started.SessionId);
        var cancelled = rig.AddGoogleSession(courseId, TimeSpan.FromDays(2));
        rig.Harness.UpdateSession(cancelled.SessionId, s => s with { Status = LiveSessionStatus.Cancelled });
        rig.AddInvitedLearner(courseId, cancelled.SessionId);

        await rig.Service().SyncAsync(Ct);

        Assert.Empty(rig.Calendar.SetCalls);
    }

    [Fact]
    public async Task Sync_ManualOnlyMode_NeverCallsGoogle()
    {
        var rig = new Rig { Mode = LiveProviderMode.ManualOnly };
        rig.ConnectInstructor();
        var courseId = Guid.NewGuid();
        var session = rig.AddGoogleSession(courseId, TimeSpan.FromDays(2));
        rig.AddInvitedLearner(courseId, session.SessionId);

        await rig.Service().SyncAsync(Ct);

        Assert.Empty(rig.Calendar.SetCalls);
    }

    // ---- The cap -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Sync_AboveTheCap_DoesNotCallGoogle_AndTellsTheInstructorOnce()
    {
        var rig = new Rig(o => o.GoogleAttendeeCap = 3);
        rig.ConnectInstructor();
        var courseId = Guid.NewGuid();
        var session = rig.AddGoogleSession(courseId, TimeSpan.FromDays(2));
        for (var i = 0; i < 4; i++)
        {
            rig.AddInvitedLearner(courseId, session.SessionId);
        }

        var first = await rig.Service().SyncAsync(Ct);
        var second = await rig.Service().SyncAsync(Ct);

        Assert.Empty(rig.Calendar.SetCalls);
        Assert.Equal(1, first.SessionsOverCap);
        Assert.Equal(1, second.SessionsOverCap);

        var alert = Assert.Single(rig.Harness.Db.Notifications);
        Assert.Equal("live.meeting_alert", alert.Type);
        Assert.Equal(rig.Harness.InstructorUserId, alert.UserId);
        Assert.Equal($"/instructor/sessions/{session.SessionId:D}", alert.LinkUrl);
        Assert.Contains("4", alert.Body, StringComparison.Ordinal);
        Assert.Contains("3", alert.Body, StringComparison.Ordinal);
        Assert.NotNull(rig.Harness.Db.Meetings.Single().ATTENDEE_SYNC_ALERT_SENT_AT_UTC);
        Assert.Empty(rig.Harness.Db.Emails); // in-app only; the calendar e-mails to learners are not this service's business
        Assert.All(rig.Harness.Db.Invites.Where(i => i.ROLE == LiveParticipantRole.Learner), i => Assert.Null(i.GOOGLE_ATTENDEE_SYNCED_AT_UTC));
    }

    [Fact]
    public async Task Sync_TheDefaultCapIs150_ExactlyAtTheCapSyncs_OneOverDoesNot()
    {
        var atCap = new Rig();
        atCap.ConnectInstructor();
        var courseA = Guid.NewGuid();
        var sessionA = atCap.AddGoogleSession(courseA, TimeSpan.FromDays(2));
        for (var i = 0; i < 150; i++)
        {
            atCap.AddInvitedLearner(courseA, sessionA.SessionId);
        }

        await atCap.Service().SyncAsync(Ct);
        Assert.Equal(150, Assert.Single(atCap.Calendar.SetCalls).Emails.Count);

        var over = new Rig();
        over.ConnectInstructor();
        var courseB = Guid.NewGuid();
        var sessionB = over.AddGoogleSession(courseB, TimeSpan.FromDays(2));
        for (var i = 0; i < 151; i++)
        {
            over.AddInvitedLearner(courseB, sessionB.SessionId);
        }

        var result = await over.Service().SyncAsync(Ct);
        Assert.Empty(over.Calendar.SetCalls);
        Assert.Equal(1, result.SessionsOverCap);
    }

    [Fact]
    public async Task Sync_WhenTheCountFallsBackUnderTheCap_ItResumes_AndAFutureCrossingAlertsAgain()
    {
        var rig = new Rig(o => o.GoogleAttendeeCap = 2);
        rig.ConnectInstructor();
        var courseId = Guid.NewGuid();
        var session = rig.AddGoogleSession(courseId, TimeSpan.FromDays(2));
        var a = rig.AddInvitedLearner(courseId, session.SessionId, TimeSpan.FromHours(3));
        var b = rig.AddInvitedLearner(courseId, session.SessionId, TimeSpan.FromHours(2));
        var c = rig.AddInvitedLearner(courseId, session.SessionId, TimeSpan.FromHours(1));

        await rig.Service().SyncAsync(Ct); // over the cap -> alert
        Assert.Single(rig.Harness.Db.Notifications);

        rig.Harness.ChangeInvite(session.SessionId, c.UserId, i => i.MarkCancelled(1, rig.Harness.Clock)); // now 2 invited
        var resumed = await rig.Service().SyncAsync(Ct);

        Assert.Equal(1, resumed.SessionsSynced);
        Assert.Equal([a.Email, b.Email], Assert.Single(rig.Calendar.SetCalls).Emails);
        Assert.Null(rig.Harness.Db.Meetings.Single().ATTENDEE_SYNC_ALERT_SENT_AT_UTC);

        var d = rig.AddInvitedLearner(courseId, session.SessionId, TimeSpan.FromMinutes(1)); // 3 again -> over again
        await rig.Service().SyncAsync(Ct);
        Assert.Equal(2, rig.Harness.Db.Notifications.Count);
        Assert.Single(rig.Calendar.SetCalls);
        _ = d;
    }

    // ---- Credentials and Google failures ----------------------------------------------------------------------

    [Fact]
    public async Task Sync_InstructorWithoutAGoogleAccount_IsSkipped_WithoutCallsOrStamps()
    {
        var rig = new Rig(); // no account connected
        var courseId = Guid.NewGuid();
        var session = rig.AddGoogleSession(courseId, TimeSpan.FromDays(2));
        var learner = rig.AddInvitedLearner(courseId, session.SessionId);

        var result = await rig.Service().SyncAsync(Ct);

        Assert.Empty(rig.Calendar.SetCalls);
        Assert.Equal(1, result.SessionsSkipped);
        Assert.Null(rig.Harness.Db.InviteFor(session.SessionId, learner.UserId)!.GOOGLE_ATTENDEE_SYNCED_AT_UTC);
    }

    /// <summary>D1: an undecryptable stored token (this host's key does not match) is skipped for the run — the account is NOT revoked and
    /// the instructor is NOT told to reconnect; the next run, with the key fixed, completes the sync.</summary>
    [Fact]
    public async Task Sync_StoredRefreshTokenCannotBeDecrypted_IsSkipped_AccountUntouched_NoAlert_AndRecoversWhenTheKeyIsFixed()
    {
        var rig = new Rig();
        var account = INSTRUCTOR_GOOGLE_ACCOUNT.Connect(
            rig.Harness.InstructorUserId, "sub-1", "teacher@gmail.test", LiveTestData.Protector().Encrypt("encrypted-with-another-key"), GoogleScopes.CalendarEventsOwned, rig.Harness.Clock);
        rig.Accounts.Accounts.Add(account);
        var courseId = Guid.NewGuid();
        var session = rig.AddGoogleSession(courseId, TimeSpan.FromDays(2));
        var learner = rig.AddInvitedLearner(courseId, session.SessionId);

        var result = await rig.Service().SyncAsync(Ct);

        Assert.Equal(1, result.SessionsSkipped);
        Assert.Empty(rig.Calendar.SetCalls);
        Assert.True(account.IsActive);
        Assert.Null(account.REVOKED_REASON);
        Assert.Empty(rig.Alerts.ReconnectAlerts);
        Assert.Equal(0, rig.Accounts.SaveCount);
        Assert.Null(rig.Harness.Db.InviteFor(session.SessionId, learner.UserId)!.GOOGLE_ATTENDEE_SYNCED_AT_UTC);

        // The key is fixed (the credential decrypts again): the very next run syncs, with no reconnect needed.
        rig.Accounts.Accounts.Clear();
        rig.ConnectInstructor();
        var retried = await rig.Service().SyncAsync(Ct);

        Assert.Equal(1, retried.SessionsSynced);
        Assert.NotNull(rig.Harness.Db.InviteFor(session.SessionId, learner.UserId)!.GOOGLE_ATTENDEE_SYNCED_AT_UTC);
    }

    [Fact]
    public async Task Sync_CalendarUnauthorized_RevokesAndAlertsOnce_AndStopsForThatInstructorInTheSameRun()
    {
        var rig = new Rig();
        rig.ConnectInstructor();
        rig.Calendar.NextResult = Result.Failure(GoogleErrors.Unauthorized("refused", "invalid_grant"));
        var courseId = Guid.NewGuid();
        var s1 = rig.AddGoogleSession(courseId, TimeSpan.FromDays(2));
        var s2 = rig.AddGoogleSession(courseId, TimeSpan.FromDays(3));
        rig.AddInvitedLearner(courseId, s1.SessionId);
        rig.AddInvitedLearner(courseId, s2.SessionId);

        var result = await rig.Service().SyncAsync(Ct);

        Assert.Single(rig.Calendar.SetCalls); // the second session is not even tried — no retry in the same run
        Assert.Equal(2, result.SessionsSkipped);
        Assert.Single(rig.Alerts.ReconnectAlerts);
        Assert.False(rig.Accounts.Accounts.Single().IsActive);
        Assert.All(rig.Harness.Db.Invites.Where(i => i.ROLE == LiveParticipantRole.Learner), i => Assert.Null(i.GOOGLE_ATTENDEE_SYNCED_AT_UTC));
    }

    [Theory]
    [InlineData(GoogleErrors.TransientCode)]
    [InlineData(GoogleErrors.RateLimitedCode)]
    [InlineData(GoogleErrors.NotFoundCode)]
    [InlineData(GoogleErrors.BadRequestCode)]
    public async Task Sync_OtherGoogleFailures_LeaveTheStampsAlone_SoTheNextRunRetries(string errorCode)
    {
        var rig = new Rig();
        rig.ConnectInstructor();
        rig.Calendar.NextResult = Result.Failure(new DomainError(errorCode, "boom"));
        var courseId = Guid.NewGuid();
        var session = rig.AddGoogleSession(courseId, TimeSpan.FromDays(2));
        var learner = rig.AddInvitedLearner(courseId, session.SessionId);

        var failed = await rig.Service().SyncAsync(Ct);

        Assert.Equal(1, failed.SessionsSkipped);
        Assert.Null(rig.Harness.Db.InviteFor(session.SessionId, learner.UserId)!.GOOGLE_ATTENDEE_SYNCED_AT_UTC);
        Assert.Empty(rig.Alerts.ReconnectAlerts);
        Assert.True(rig.Accounts.Accounts.Single().IsActive); // a transient problem never revokes the credential

        rig.Calendar.NextResult = Result.Success();
        var retried = await rig.Service().SyncAsync(Ct);

        Assert.Equal(1, retried.SessionsSynced);
        Assert.NotNull(rig.Harness.Db.InviteFor(session.SessionId, learner.UserId)!.GOOGLE_ATTENDEE_SYNCED_AT_UTC);
    }

    [Fact]
    public async Task Sync_AFailedSave_RollsBack_AndTheNextRunCompletesIt()
    {
        var rig = new Rig();
        rig.ConnectInstructor();
        var courseId = Guid.NewGuid();
        var session = rig.AddGoogleSession(courseId, TimeSpan.FromDays(2));
        var learner = rig.AddInvitedLearner(courseId, session.SessionId);
        rig.Harness.Db.ThrowOnNextSave = new InvalidOperationException("db down");

        var failed = await rig.Service().SyncAsync(Ct);

        Assert.Equal(1, failed.SessionsFailed);
        Assert.Null(rig.Harness.Db.InviteFor(session.SessionId, learner.UserId)!.GOOGLE_ATTENDEE_SYNCED_AT_UTC);

        var ok = await rig.Service().SyncAsync(Ct);

        Assert.Equal(1, ok.SessionsSynced);
        Assert.NotNull(rig.Harness.Db.InviteFor(session.SessionId, learner.UserId)!.GOOGLE_ATTENDEE_SYNCED_AT_UTC);
        Assert.Equal(2, rig.Calendar.SetCalls.Count); // Google is idempotent for an identical list; the stamps were what was lost
    }

    // ---- Volume and privacy ---------------------------------------------------------------------------------

    [Fact]
    public async Task Sync_HandlesAtMostTwentySessionsPerRun_AndTheNextRunTakesTheRest()
    {
        var rig = new Rig();
        rig.ConnectInstructor();
        for (var i = 0; i < GoogleAttendeeSyncService.MaxSessionsPerRun + 5; i++)
        {
            var courseId = Guid.NewGuid();
            var session = rig.AddGoogleSession(courseId, TimeSpan.FromDays(2) + TimeSpan.FromMinutes(i));
            rig.AddInvitedLearner(courseId, session.SessionId);
        }

        var first = await rig.Service().SyncAsync(Ct);
        var second = await rig.Service().SyncAsync(Ct);

        Assert.Equal(GoogleAttendeeSyncService.MaxSessionsPerRun, first.SessionsSynced);
        Assert.Equal(5, second.SessionsSynced);
        Assert.Equal(GoogleAttendeeSyncService.MaxSessionsPerRun + 5, rig.Calendar.SetCalls.Select(c => c.EventId).Count());
    }

    [Fact]
    public async Task Sync_NeverLogsALearnersAddress()
    {
        var rig = new Rig();
        rig.ConnectInstructor();
        var courseId = Guid.NewGuid();
        var session = rig.AddGoogleSession(courseId, TimeSpan.FromDays(2));
        var learner = rig.AddInvitedLearner(courseId, session.SessionId);
        rig.Calendar.NextResult = Result.Failure(GoogleErrors.Transient("boom"));

        await rig.Service().SyncAsync(Ct);
        rig.Calendar.NextResult = Result.Success();
        await rig.Service().SyncAsync(Ct);

        Assert.DoesNotContain(learner.Email, rig.Logger.All, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(learner.Email, rig.AccountLogger.All, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(learner.Email, string.Join("\n", rig.Harness.Db.Notifications.Select(n => n.Body)), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Sync_NothingEnabled_DoesNothingAtAll()
    {
        var rig = new Rig();

        var result = await rig.Service().SyncAsync(Ct);

        Assert.Equal(new AttendeeSyncResult(0, 0, 0, 0), result);
        Assert.Empty(rig.Calendar.SetCalls);
    }
}
