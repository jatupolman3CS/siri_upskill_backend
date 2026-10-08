using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Live.Application;

/// <summary>Which reminder an invite is due for right now.</summary>
public enum ReminderKind
{
    None,

    /// <summary>The "tomorrow" reminder: between one and 24 hours before the start.</summary>
    TwentyFourHours,

    /// <summary>The "one hour to go" reminder: within the last hour before the start.</summary>
    OneHour,
}

/// <summary>
/// The pure decisions of the invite/reminder jobs (docs/contracts/P11-04-live-invites-ics-reminders.md §4.4/§4.5) — no I/O, no
/// clock of its own — so the timing rules can be unit-tested at their exact edges. The services call these and then act.
/// </summary>
public static class SessionInvitePolicy
{
    /// <summary>How far before the start the "tomorrow" reminder is sent.</summary>
    public static readonly TimeSpan Reminder24hLead = TimeSpan.FromHours(24);

    /// <summary>How far before the start the "one hour" reminder is sent.</summary>
    public static readonly TimeSpan Reminder1hLead = TimeSpan.FromHours(1);

    /// <summary>A session learners can still be invited to: scheduled and not started yet. A session that has begun is never
    /// invited to (a latecomer still watches the recording through the episode's own entitlement).</summary>
    public static bool IsUpcoming(LiveSessionContext session, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(session);
        return session.Status == LiveSessionStatus.Scheduled && session.StartsAtUtc > now;
    }

    /// <summary>
    /// The reminder (if any) an <see cref="InviteStatus.Invited"/> invite is due for at <paramref name="now"/>.
    /// <list type="bullet">
    /// <item><b>24 h:</b> remaining time in <c>(1 h, 24 h]</c>, not yet sent, and the invite went out no later than 24 h before
    /// the start — an invitation sent inside the last 24 h already told the learner, so a "tomorrow" reminder would be noise.</item>
    /// <item><b>1 h:</b> remaining time in <c>(0, 1 h]</c>, not yet sent, and the invite went out no later than an hour before the
    /// start.</item>
    /// </list>
    /// A job that was down until the window had passed simply skips the reminder (never a late "tomorrow" email).
    /// </summary>
    public static ReminderKind DueReminder(SESSION_INVITE invite, LiveSessionContext session, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(invite);
        ArgumentNullException.ThrowIfNull(session);

        if (invite.STATUS != InviteStatus.Invited || session.Status != LiveSessionStatus.Scheduled)
        {
            return ReminderKind.None;
        }

        var remaining = session.StartsAtUtc - now;
        if (remaining <= TimeSpan.Zero || invite.INVITE_SENT_AT_UTC is not { } invitedAt)
        {
            return ReminderKind.None;
        }

        if (invite.REMINDER_24H_SENT_AT_UTC is null
            && remaining > Reminder1hLead
            && remaining <= Reminder24hLead
            && invitedAt <= session.StartsAtUtc - Reminder24hLead)
        {
            return ReminderKind.TwentyFourHours;
        }

        if (invite.REMINDER_1H_SENT_AT_UTC is null
            && remaining <= Reminder1hLead
            && invitedAt <= session.StartsAtUtc - Reminder1hLead)
        {
            return ReminderKind.OneHour;
        }

        return ReminderKind.None;
    }

    /// <summary>
    /// Marks the reminders whose moment has already passed as sent, right after an invitation or an update e-mail went out: that
    /// e-mail is the notice for them. Without this a re-invited learner (whose first-invite time is kept) or a learner whose
    /// session was moved closer would get a "tomorrow" reminder straight after the e-mail that just told them. Must be called while
    /// the invite is <see cref="InviteStatus.Invited"/>.
    /// </summary>
    public static void SuppressElapsedReminders(SESSION_INVITE invite, LiveSessionContext session, DateTime now, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(invite);
        ArgumentNullException.ThrowIfNull(session);
        ArgumentNullException.ThrowIfNull(clock);

        var remaining = session.StartsAtUtc - now;

        if (remaining <= Reminder24hLead && invite.REMINDER_24H_SENT_AT_UTC is null)
        {
            invite.MarkReminder24h(clock);
        }

        if (remaining <= Reminder1hLead && invite.REMINDER_1H_SENT_AT_UTC is null)
        {
            invite.MarkReminder1h(clock);
        }
    }

    /// <summary>A learner's first invitation in a course: they have no <see cref="InviteStatus.Invited"/> invite there yet
    /// (also true again after they lost access and bought the course again).</summary>
    public static bool IsFirstInvite(IEnumerable<SESSION_INVITE> userInvitesInCourse)
    {
        ArgumentNullException.ThrowIfNull(userInvitesInCourse);
        return !userInvitesInCourse.Any(i => i.STATUS == InviteStatus.Invited);
    }

    /// <summary>Whether an invited session has been moved/renamed since the last e-mail: the meeting's SEQUENCE is ahead of the one
    /// sent. A session that never had its sequence bumped (<c>0</c>) never needs an update.</summary>
    public static bool NeedsUpdate(SESSION_INVITE invite, int meetingSequence)
    {
        ArgumentNullException.ThrowIfNull(invite);
        return invite.STATUS == InviteStatus.Invited && (invite.ICS_SEQUENCE_SENT ?? -1) < meetingSequence;
    }

    /// <summary>Whether the room of a session is a Google Calendar event on the instructor's own calendar — in that case Google
    /// keeps their calendar current and a calendar file from us would create a duplicate entry (contract §0 item 5).</summary>
    public static bool IsGoogleEvent(SESSION_MEETING? meeting) => meeting?.PROVIDER == MeetingProvider.GoogleMeet;

    /// <summary>The room situation of a session needs an instructor warning: no meeting row is handled by the caller, here the
    /// row exists but cannot be used. (Used by the T-24 h readiness alert.)</summary>
    public static bool NeedsReadinessAlert(SESSION_MEETING meeting, LiveSessionContext session, DateTime now)
    {
        ArgumentNullException.ThrowIfNull(meeting);
        ArgumentNullException.ThrowIfNull(session);

        var remaining = session.StartsAtUtc - now;
        return session.Status == LiveSessionStatus.Scheduled
            && remaining > TimeSpan.Zero
            && remaining <= Reminder24hLead
            && !meeting.IsUsable
            && meeting.READINESS_ALERT_SENT_AT_UTC is null;
    }
}
