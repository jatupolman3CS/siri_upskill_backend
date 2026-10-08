using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;

namespace Siri.UnitTests.Live;

/// <summary>Timing and decision rules of the invite/reminder jobs, asserted at their exact edges
/// (docs/contracts/P11-04-live-invites-ics-reminders.md §4.4/§4.5).</summary>
public class SessionInvitePolicyTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 3, 0, 0, DateTimeKind.Utc);

    private static LiveSessionContext Session(TimeSpan startsIn, LiveSessionStatus status = LiveSessionStatus.Scheduled) =>
        LiveTestData.Context(status: status, startsAtUtc: Now + startsIn, endsAtUtc: Now + startsIn + TimeSpan.FromHours(2));

    /// <summary>An invite that was sent <paramref name="sentAgo"/> before <see cref="Now"/>.</summary>
    private static SESSION_INVITE InvitedAgo(TimeSpan sentAgo, int sequence = 0)
    {
        var invite = SESSION_INVITE.Create(Guid.NewGuid(), Guid.NewGuid(), LiveParticipantRole.Learner, new FakeClock(Now - sentAgo));
        invite.MarkInvited(sequence, new FakeClock(Now - sentAgo));
        return invite;
    }

    // ---- IsUpcoming ---------------------------------------------------------------------------------

    [Fact]
    public void IsUpcoming_OnlyAScheduledSessionThatHasNotStarted()
    {
        Assert.True(SessionInvitePolicy.IsUpcoming(Session(TimeSpan.FromMinutes(1)), Now));
        Assert.False(SessionInvitePolicy.IsUpcoming(Session(TimeSpan.Zero), Now)); // starts exactly now = started
        Assert.False(SessionInvitePolicy.IsUpcoming(Session(TimeSpan.FromMinutes(-1)), Now));
        Assert.False(SessionInvitePolicy.IsUpcoming(Session(TimeSpan.FromDays(1), LiveSessionStatus.Cancelled), Now));
    }

    // ---- DueReminder: the 24-hour window --------------------------------------------------------------

    [Fact]
    public void DueReminder_ExactlyTwentyFourHoursBefore_Is24h()
    {
        var invite = InvitedAgo(TimeSpan.FromDays(3));

        Assert.Equal(ReminderKind.TwentyFourHours, SessionInvitePolicy.DueReminder(invite, Session(TimeSpan.FromHours(24)), Now));
    }

    [Fact]
    public void DueReminder_OneMillisecondOutsideTheTwentyFourHourWindow_IsNone()
    {
        var invite = InvitedAgo(TimeSpan.FromDays(3));

        Assert.Equal(ReminderKind.None, SessionInvitePolicy.DueReminder(invite, Session(TimeSpan.FromHours(24) + TimeSpan.FromMilliseconds(1)), Now));
        Assert.Equal(ReminderKind.TwentyFourHours, SessionInvitePolicy.DueReminder(invite, Session(TimeSpan.FromHours(24) - TimeSpan.FromMilliseconds(1)), Now));
    }

    [Fact]
    public void DueReminder_JustOverOneHour_Is24h_AndExactlyOneHour_Is1h()
    {
        var invite = InvitedAgo(TimeSpan.FromDays(3));

        Assert.Equal(ReminderKind.TwentyFourHours, SessionInvitePolicy.DueReminder(invite, Session(TimeSpan.FromHours(1) + TimeSpan.FromMilliseconds(1)), Now));
        Assert.Equal(ReminderKind.OneHour, SessionInvitePolicy.DueReminder(invite, Session(TimeSpan.FromHours(1)), Now));
    }

    [Fact]
    public void DueReminder_AtOrAfterTheStart_IsNone()
    {
        var invite = InvitedAgo(TimeSpan.FromDays(3));

        Assert.Equal(ReminderKind.OneHour, SessionInvitePolicy.DueReminder(invite, Session(TimeSpan.FromMilliseconds(1)), Now));
        Assert.Equal(ReminderKind.None, SessionInvitePolicy.DueReminder(invite, Session(TimeSpan.Zero), Now));
        Assert.Equal(ReminderKind.None, SessionInvitePolicy.DueReminder(invite, Session(TimeSpan.FromMinutes(-5)), Now));
    }

    [Fact]
    public void DueReminder_AnInviteSentInsideTheLast24Hours_SkipsThe24hReminder()
    {
        // session in 20 h; invited 2 h ago => the invitation already told the learner, a "tomorrow" reminder would be noise
        var invite = InvitedAgo(TimeSpan.FromHours(2));

        Assert.Equal(ReminderKind.None, SessionInvitePolicy.DueReminder(invite, Session(TimeSpan.FromHours(20)), Now));
    }

    [Fact]
    public void DueReminder_AnInviteSentExactlyAtThe24hPoint_StillGets24h()
    {
        // session in 20 h => the 24 h point was 4 h ago; invited exactly then (<=) -> due
        var invite = InvitedAgo(TimeSpan.FromHours(4));

        Assert.Equal(ReminderKind.TwentyFourHours, SessionInvitePolicy.DueReminder(invite, Session(TimeSpan.FromHours(20)), Now));
        Assert.Equal(ReminderKind.None, SessionInvitePolicy.DueReminder(InvitedAgo(TimeSpan.FromHours(4) - TimeSpan.FromMilliseconds(1)), Session(TimeSpan.FromHours(20)), Now));
    }

    // ---- DueReminder: the 1-hour window -----------------------------------------------------------------

    [Fact]
    public void DueReminder_AnInviteSentInsideTheLastHour_SkipsThe1hReminder()
    {
        var invite = InvitedAgo(TimeSpan.FromMinutes(10));

        Assert.Equal(ReminderKind.None, SessionInvitePolicy.DueReminder(invite, Session(TimeSpan.FromMinutes(30)), Now));
    }

    [Fact]
    public void DueReminder_AnInviteSentJustBeforeTheOneHourPoint_Gets1h()
    {
        // session in 30 min => the 1 h point was 30 min ago; invited 31 min ago (before it) -> due
        Assert.Equal(ReminderKind.OneHour, SessionInvitePolicy.DueReminder(InvitedAgo(TimeSpan.FromMinutes(31)), Session(TimeSpan.FromMinutes(30)), Now));
        Assert.Equal(ReminderKind.OneHour, SessionInvitePolicy.DueReminder(InvitedAgo(TimeSpan.FromMinutes(30)), Session(TimeSpan.FromMinutes(30)), Now));
        Assert.Equal(ReminderKind.None, SessionInvitePolicy.DueReminder(InvitedAgo(TimeSpan.FromMinutes(29)), Session(TimeSpan.FromMinutes(30)), Now));
    }

    // ---- DueReminder: flags and states --------------------------------------------------------------------

    [Fact]
    public void DueReminder_AlreadySent_IsNone_SoARerunNeverDoubleSends()
    {
        var clock = new FakeClock(Now);

        var sent24 = InvitedAgo(TimeSpan.FromDays(3));
        sent24.MarkReminder24h(clock);
        Assert.Equal(ReminderKind.None, SessionInvitePolicy.DueReminder(sent24, Session(TimeSpan.FromHours(10)), Now));

        var sent1 = InvitedAgo(TimeSpan.FromDays(3));
        sent1.MarkReminder1h(clock);
        Assert.Equal(ReminderKind.None, SessionInvitePolicy.DueReminder(sent1, Session(TimeSpan.FromMinutes(30)), Now));
    }

    [Fact]
    public void DueReminder_NotInvitedOrACancelledSession_IsNone()
    {
        var pending = SESSION_INVITE.Create(Guid.NewGuid(), Guid.NewGuid(), LiveParticipantRole.Learner, new FakeClock(Now));
        Assert.Equal(ReminderKind.None, SessionInvitePolicy.DueReminder(pending, Session(TimeSpan.FromHours(10)), Now));

        var cancelledInvite = InvitedAgo(TimeSpan.FromDays(3));
        cancelledInvite.MarkCancelled(1, new FakeClock(Now));
        Assert.Equal(ReminderKind.None, SessionInvitePolicy.DueReminder(cancelledInvite, Session(TimeSpan.FromHours(10)), Now));

        var skipped = InvitedAgo(TimeSpan.FromDays(3));
        skipped.MarkSkipped("no_contact");
        Assert.Equal(ReminderKind.None, SessionInvitePolicy.DueReminder(skipped, Session(TimeSpan.FromHours(10)), Now));

        Assert.Equal(
            ReminderKind.None,
            SessionInvitePolicy.DueReminder(InvitedAgo(TimeSpan.FromDays(3)), Session(TimeSpan.FromHours(10), LiveSessionStatus.Cancelled), Now));
    }

    [Fact]
    public void DueReminder_AfterAReschedule_BecomesDueAgainAtTheNewTime()
    {
        var invite = InvitedAgo(TimeSpan.FromDays(5));
        invite.MarkReminder24h(new FakeClock(Now)); // sent for the old time

        Assert.Equal(ReminderKind.None, SessionInvitePolicy.DueReminder(invite, Session(TimeSpan.FromHours(10)), Now));

        invite.ResetReminders(); // the session moved

        Assert.Equal(ReminderKind.TwentyFourHours, SessionInvitePolicy.DueReminder(invite, Session(TimeSpan.FromHours(10)), Now));
    }

    // ---- SuppressElapsedReminders -----------------------------------------------------------------------

    [Fact]
    public void SuppressElapsedReminders_MarksOnlyTheMomentsThatHavePassed()
    {
        var clock = new FakeClock(Now);

        var far = InvitedAgo(TimeSpan.Zero);
        SessionInvitePolicy.SuppressElapsedReminders(far, Session(TimeSpan.FromDays(3)), Now, clock);
        Assert.Null(far.REMINDER_24H_SENT_AT_UTC);
        Assert.Null(far.REMINDER_1H_SENT_AT_UTC);

        var tomorrow = InvitedAgo(TimeSpan.Zero);
        SessionInvitePolicy.SuppressElapsedReminders(tomorrow, Session(TimeSpan.FromHours(10)), Now, clock);
        Assert.NotNull(tomorrow.REMINDER_24H_SENT_AT_UTC);
        Assert.Null(tomorrow.REMINDER_1H_SENT_AT_UTC);

        var soon = InvitedAgo(TimeSpan.Zero);
        SessionInvitePolicy.SuppressElapsedReminders(soon, Session(TimeSpan.FromMinutes(30)), Now, clock);
        Assert.NotNull(soon.REMINDER_24H_SENT_AT_UTC);
        Assert.NotNull(soon.REMINDER_1H_SENT_AT_UTC);
    }

    // ---- IsFirstInvite / NeedsUpdate -----------------------------------------------------------------------

    [Fact]
    public void IsFirstInvite_TrueUntilTheUserHasAnInvitedInvite()
    {
        var pending = SESSION_INVITE.Create(Guid.NewGuid(), Guid.NewGuid(), LiveParticipantRole.Learner, new FakeClock(Now));
        var cancelled = InvitedAgo(TimeSpan.FromDays(1));
        cancelled.MarkCancelled(1, new FakeClock(Now));

        Assert.True(SessionInvitePolicy.IsFirstInvite([pending]));
        Assert.True(SessionInvitePolicy.IsFirstInvite([pending, cancelled])); // lost access, bought again
        Assert.False(SessionInvitePolicy.IsFirstInvite([pending, InvitedAgo(TimeSpan.FromDays(1))]));
    }

    [Fact]
    public void NeedsUpdate_OnlyWhenTheMeetingSequenceIsAheadOfTheOneSent()
    {
        var invite = InvitedAgo(TimeSpan.FromDays(1), sequence: 2);

        Assert.False(SessionInvitePolicy.NeedsUpdate(invite, 0));
        Assert.False(SessionInvitePolicy.NeedsUpdate(invite, 2));
        Assert.True(SessionInvitePolicy.NeedsUpdate(invite, 3));

        var pending = SESSION_INVITE.Create(Guid.NewGuid(), Guid.NewGuid(), LiveParticipantRole.Learner, new FakeClock(Now));
        Assert.False(SessionInvitePolicy.NeedsUpdate(pending, 5)); // nothing was ever sent — the Pending path handles it
    }

    // ---- Readiness alert --------------------------------------------------------------------------------------

    [Fact]
    public void NeedsReadinessAlert_OnlyForAnUnusableRoom_WithinADay_AndNotAlertedYet()
    {
        var awaiting = SESSION_MEETING.Stage(Guid.NewGuid());
        awaiting.ResolveAsAwaitingLink();

        Assert.True(SessionInvitePolicy.NeedsReadinessAlert(awaiting, Session(TimeSpan.FromHours(24)), Now));
        Assert.False(SessionInvitePolicy.NeedsReadinessAlert(awaiting, Session(TimeSpan.FromHours(24) + TimeSpan.FromMilliseconds(1)), Now));
        Assert.False(SessionInvitePolicy.NeedsReadinessAlert(awaiting, Session(TimeSpan.Zero), Now));
        Assert.False(SessionInvitePolicy.NeedsReadinessAlert(awaiting, Session(TimeSpan.FromHours(5), LiveSessionStatus.Cancelled), Now));

        awaiting.MarkReadinessAlertSent(new FakeClock(Now));
        Assert.False(SessionInvitePolicy.NeedsReadinessAlert(awaiting, Session(TimeSpan.FromHours(5)), Now));

        var usable = SESSION_MEETING.Stage(Guid.NewGuid());
        usable.SetManualLink("cipher");
        Assert.False(SessionInvitePolicy.NeedsReadinessAlert(usable, Session(TimeSpan.FromHours(5)), Now));
    }

    [Fact]
    public void NeedsReadinessAlert_IsIndependentOfThePasteALinkAlertFlag()
    {
        // MEETING_ALERT_SENT_AT_UTC is consumed by the "paste a link" alert days earlier; the T-24h warning has its own stamp.
        var meeting = SESSION_MEETING.Stage(Guid.NewGuid());
        meeting.ResolveAsAwaitingLink();
        meeting.MarkAlertSent(new FakeClock(Now.AddDays(-3)));

        Assert.True(SessionInvitePolicy.NeedsReadinessAlert(meeting, Session(TimeSpan.FromHours(20)), Now));
    }

    [Fact]
    public void IsGoogleEvent_OnlyForTheGoogleMeetProvider()
    {
        var manual = SESSION_MEETING.Stage(Guid.NewGuid());
        manual.SetManualLink("cipher");
        var google = SESSION_MEETING.Stage(Guid.NewGuid());
        google.AssignProvider(MeetingProvider.GoogleMeet, Guid.NewGuid());

        Assert.False(SessionInvitePolicy.IsGoogleEvent(null));
        Assert.False(SessionInvitePolicy.IsGoogleEvent(manual));
        Assert.True(SessionInvitePolicy.IsGoogleEvent(google));
    }
}
