using Siri.Modules.Live.Domain;

namespace Siri.UnitTests.Live;

/// <summary>Unit tests for <see cref="SESSION_INVITE"/> (docs/contracts/P11-04-live-invites-ics-reminders.md §2.2).</summary>
public class SessionInviteTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 3, 0, 0, DateTimeKind.Utc);

    private static SESSION_INVITE NewInvite(LiveParticipantRole role = LiveParticipantRole.Learner, FakeClock? clock = null) =>
        SESSION_INVITE.Create(Guid.NewGuid(), Guid.NewGuid(), role, clock ?? new FakeClock(Now));

    private static SESSION_INVITE InvitedAt(int sequence, FakeClock clock)
    {
        var invite = NewInvite(clock: clock);
        invite.MarkInvited(sequence, clock);
        return invite;
    }

    // ---- Create --------------------------------------------------------------------------------

    [Theory]
    [InlineData(LiveParticipantRole.Learner)]
    [InlineData(LiveParticipantRole.Instructor)]
    public void Create_ValidInput_StartsPendingWithNothingSent(LiveParticipantRole role)
    {
        var sessionId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var invite = SESSION_INVITE.Create(sessionId, userId, role, new FakeClock(Now));

        Assert.NotEqual(Guid.Empty, invite.SESSION_INVITE_ID);
        Assert.Equal(sessionId, invite.SESSION_ID);
        Assert.Equal(userId, invite.USER_ID);
        Assert.Equal(role, invite.ROLE);
        Assert.Equal(InviteStatus.Pending, invite.STATUS);
        Assert.Null(invite.ICS_SEQUENCE_SENT);
        Assert.Null(invite.INVITE_SENT_AT_UTC);
        Assert.Null(invite.CANCEL_SENT_AT_UTC);
        Assert.Null(invite.REMINDER_24H_SENT_AT_UTC);
        Assert.Null(invite.REMINDER_1H_SENT_AT_UTC);
        Assert.Null(invite.GOOGLE_ATTENDEE_SYNCED_AT_UTC);
        Assert.Null(invite.ERROR);
    }

    [Fact]
    public void Create_EmptyIds_Throw()
    {
        Assert.Throws<ArgumentException>(() => SESSION_INVITE.Create(Guid.Empty, Guid.NewGuid(), LiveParticipantRole.Learner, new FakeClock(Now)));
        Assert.Throws<ArgumentException>(() => SESSION_INVITE.Create(Guid.NewGuid(), Guid.Empty, LiveParticipantRole.Learner, new FakeClock(Now)));
    }

    // ---- MarkInvited ---------------------------------------------------------------------------

    [Fact]
    public void MarkInvited_FromPending_RecordsSequenceAndFirstInviteTime()
    {
        var clock = new FakeClock(Now);
        var invite = NewInvite(clock: clock);

        invite.MarkInvited(0, clock);

        Assert.Equal(InviteStatus.Invited, invite.STATUS);
        Assert.Equal(0, invite.ICS_SEQUENCE_SENT);
        Assert.Equal(Now, invite.INVITE_SENT_AT_UTC);
        Assert.Null(invite.ERROR);
    }

    [Fact]
    public void MarkInvited_Again_KeepsFirstInviteTimeAndRaisesSequence()
    {
        var clock = new FakeClock(Now);
        var invite = InvitedAt(0, clock);
        clock.UtcNow = Now.AddDays(2);

        invite.MarkInvited(3, clock);

        Assert.Equal(3, invite.ICS_SEQUENCE_SENT);
        Assert.Equal(Now, invite.INVITE_SENT_AT_UTC);
    }

    [Fact]
    public void MarkInvited_SequenceGoingBackwards_Throws()
    {
        var clock = new FakeClock(Now);
        var invite = InvitedAt(4, clock);

        Assert.Throws<ArgumentOutOfRangeException>(() => invite.MarkInvited(3, clock));
        Assert.Equal(4, invite.ICS_SEQUENCE_SENT);
    }

    [Fact]
    public void MarkInvited_NegativeSequence_Throws()
    {
        var clock = new FakeClock(Now);
        var invite = NewInvite(clock: clock);

        Assert.Throws<ArgumentOutOfRangeException>(() => invite.MarkInvited(-1, clock));
    }

    [Fact]
    public void MarkInvited_FromSkipped_Throws()
    {
        var clock = new FakeClock(Now);
        var invite = NewInvite(clock: clock);
        invite.MarkSkipped("no_contact");

        Assert.Throws<InvalidOperationException>(() => invite.MarkInvited(1, clock));
        Assert.Equal(InviteStatus.Skipped, invite.STATUS);
        Assert.Equal("no_contact", invite.ERROR);
    }

    [Fact]
    public void MarkInvited_FromCancelled_Throws()
    {
        var clock = new FakeClock(Now);
        var invite = InvitedAt(0, clock);
        invite.MarkCancelled(1, clock);

        Assert.Throws<InvalidOperationException>(() => invite.MarkInvited(2, clock));
        Assert.Equal(InviteStatus.Cancelled, invite.STATUS);
    }

    // ---- MarkCancelled -------------------------------------------------------------------------

    [Fact]
    public void MarkCancelled_FromInvited_RecordsCancelAndSequence()
    {
        var clock = new FakeClock(Now);
        var invite = InvitedAt(0, clock);
        clock.UtcNow = Now.AddDays(1);

        invite.MarkCancelled(1, clock);

        Assert.Equal(InviteStatus.Cancelled, invite.STATUS);
        Assert.Equal(Now.AddDays(1), invite.CANCEL_SENT_AT_UTC);
        Assert.Equal(1, invite.ICS_SEQUENCE_SENT);
        Assert.Equal(Now, invite.INVITE_SENT_AT_UTC);
    }

    [Fact]
    public void MarkCancelled_FromPending_IsAllowed()
    {
        var clock = new FakeClock(Now);
        var invite = NewInvite(clock: clock);

        invite.MarkCancelled(0, clock);

        Assert.Equal(InviteStatus.Cancelled, invite.STATUS);
    }

    [Fact]
    public void MarkCancelled_SequenceBelowLastSent_Throws()
    {
        var clock = new FakeClock(Now);
        var invite = InvitedAt(5, clock);

        Assert.Throws<ArgumentOutOfRangeException>(() => invite.MarkCancelled(4, clock));
        Assert.Equal(InviteStatus.Invited, invite.STATUS);
    }

    [Fact]
    public void MarkCancelled_AlreadyCancelled_Throws()
    {
        var clock = new FakeClock(Now);
        var invite = InvitedAt(0, clock);
        invite.MarkCancelled(1, clock);

        Assert.Throws<InvalidOperationException>(() => invite.MarkCancelled(2, clock));
    }

    // ---- Reinvite ------------------------------------------------------------------------------

    [Fact]
    public void Reinvite_FromCancelled_ReturnsToPendingAndClearsStampsButKeepsSequence()
    {
        var clock = new FakeClock(Now);
        var invite = InvitedAt(2, clock);
        invite.MarkReminder24h(clock);
        invite.MarkReminder1h(clock);
        invite.MarkAttendeeSynced(clock);
        invite.MarkCancelled(3, clock);

        invite.Reinvite();

        Assert.Equal(InviteStatus.Pending, invite.STATUS);
        Assert.Null(invite.CANCEL_SENT_AT_UTC);
        Assert.Null(invite.REMINDER_24H_SENT_AT_UTC);
        Assert.Null(invite.REMINDER_1H_SENT_AT_UTC);
        Assert.Null(invite.GOOGLE_ATTENDEE_SYNCED_AT_UTC);
        Assert.Equal(3, invite.ICS_SEQUENCE_SENT);
        Assert.Equal(Now, invite.INVITE_SENT_AT_UTC);
    }

    [Theory]
    [InlineData(InviteStatus.Pending)]
    [InlineData(InviteStatus.Invited)]
    [InlineData(InviteStatus.Skipped)]
    public void Reinvite_FromAnythingButCancelled_Throws(InviteStatus status)
    {
        var clock = new FakeClock(Now);
        var invite = NewInvite(clock: clock);
        switch (status)
        {
            case InviteStatus.Invited:
                invite.MarkInvited(0, clock);
                break;
            case InviteStatus.Skipped:
                invite.MarkSkipped("no_contact");
                break;
        }

        Assert.Throws<InvalidOperationException>(() => invite.Reinvite());
        Assert.Equal(status, invite.STATUS);
    }

    // ---- NextSequence --------------------------------------------------------------------------

    [Theory]
    [InlineData(null, 0, 0)]       // nothing sent yet: the meeting's own sequence
    [InlineData(null, 3, 3)]
    [InlineData(0, 0, 1)]          // already sent 0 and the session did not change: still strictly higher
    [InlineData(2, 1, 3)]          // meeting sequence below what this invite already sent
    [InlineData(2, 5, 5)]          // session changed several times since
    public void NextSequence_IsMonotonicAndNeverBelowTheMeetingSequence(int? alreadySent, int meetingSequence, int expected)
    {
        var clock = new FakeClock(Now);
        var invite = NewInvite(clock: clock);
        if (alreadySent is { } sent)
        {
            invite.MarkInvited(sent, clock);
        }

        Assert.Equal(expected, invite.NextSequence(meetingSequence));
    }

    [Fact]
    public void NextSequence_AfterCancelAndReinvite_StaysAboveTheCancelSequence()
    {
        var clock = new FakeClock(Now);
        var invite = InvitedAt(0, clock);
        invite.MarkCancelled(1, clock);
        invite.Reinvite();

        Assert.Equal(2, invite.NextSequence(1));
    }

    // ---- MarkSkipped ---------------------------------------------------------------------------

    [Fact]
    public void MarkSkipped_FromPending_RecordsTheCode()
    {
        var invite = NewInvite();

        invite.MarkSkipped("no_contact");

        Assert.Equal(InviteStatus.Skipped, invite.STATUS);
        Assert.Equal("no_contact", invite.ERROR);
    }

    [Fact]
    public void MarkSkipped_BlankCode_Throws()
    {
        var invite = NewInvite();

        Assert.Throws<ArgumentException>(() => invite.MarkSkipped(" "));
    }

    [Fact]
    public void MarkSkipped_OverlongCode_IsTruncatedToTheColumn()
    {
        var invite = NewInvite();

        invite.MarkSkipped(new string('c', 500));

        Assert.Equal(300, invite.ERROR!.Length);
    }

    [Fact]
    public void MarkSkipped_FromCancelled_Throws()
    {
        var clock = new FakeClock(Now);
        var invite = InvitedAt(0, clock);
        invite.MarkCancelled(1, clock);

        Assert.Throws<InvalidOperationException>(() => invite.MarkSkipped("no_contact"));
    }

    // ---- Reminders -----------------------------------------------------------------------------

    [Fact]
    public void MarkReminders_OnInvitedInvite_StampOnlyTheFirstSend()
    {
        var clock = new FakeClock(Now);
        var invite = InvitedAt(0, clock);

        invite.MarkReminder24h(clock);
        clock.UtcNow = Now.AddHours(23);
        invite.MarkReminder1h(clock);
        clock.UtcNow = Now.AddHours(24);
        invite.MarkReminder24h(clock);

        Assert.Equal(Now, invite.REMINDER_24H_SENT_AT_UTC);
        Assert.Equal(Now.AddHours(23), invite.REMINDER_1H_SENT_AT_UTC);
    }

    [Fact]
    public void MarkReminders_OnPendingInvite_Throw()
    {
        var clock = new FakeClock(Now);
        var invite = NewInvite(clock: clock);

        Assert.Throws<InvalidOperationException>(() => invite.MarkReminder24h(clock));
        Assert.Throws<InvalidOperationException>(() => invite.MarkReminder1h(clock));
    }

    [Fact]
    public void ResetReminders_ClearsBothStamps()
    {
        var clock = new FakeClock(Now);
        var invite = InvitedAt(0, clock);
        invite.MarkReminder24h(clock);
        invite.MarkReminder1h(clock);

        invite.ResetReminders();

        Assert.Null(invite.REMINDER_24H_SENT_AT_UTC);
        Assert.Null(invite.REMINDER_1H_SENT_AT_UTC);
        Assert.Equal(InviteStatus.Invited, invite.STATUS);
    }

    // ---- Google attendee stamp -----------------------------------------------------------------

    [Fact]
    public void AttendeeSync_StampAndClear()
    {
        var clock = new FakeClock(Now);
        var invite = InvitedAt(0, clock);

        invite.MarkAttendeeSynced(clock);
        Assert.Equal(Now, invite.GOOGLE_ATTENDEE_SYNCED_AT_UTC);

        invite.ClearAttendeeSynced();
        Assert.Null(invite.GOOGLE_ATTENDEE_SYNCED_AT_UTC);
    }
}
