using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;

namespace Siri.UnitTests.Live;

/// <summary>
/// The 24 h / 1 h reminders to learners and instructors and the T-24 h "room not ready" warning
/// (docs/contracts/P11-04-live-invites-ics-reminders.md §4.5). Idempotency is the headline property: a reminder is sent in the same unit of
/// work as the flag that records it, so a re-run — or a crash between runs — can never double-send.
/// </summary>
public class SessionReminderTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    /// <summary>A course with one session starting <paramref name="startsIn"/> from now, a Manual room, an invited learner and an invited instructor.</summary>
    private static (InviteHarness H, LiveSessionContext Session, Guid LearnerId, string LearnerEmail, string InstructorEmail) Arrange(
        TimeSpan startsIn, TimeSpan invitedAgo, bool googleRoom = false)
    {
        var h = new InviteHarness();
        var instructorEmail = h.AddInstructorContact();
        var courseId = Guid.NewGuid();
        var session = h.AddSession(courseId, startsIn);
        if (googleRoom)
        {
            h.AddGoogleMeeting(session.SessionId);
        }
        else
        {
            h.AddManualMeeting(session.SessionId);
        }

        var (learnerId, learnerEmail) = h.AddLearner(courseId);
        h.SeedInvited(session.SessionId, learnerId, LiveParticipantRole.Learner, invitedAgo, sequence: 2);
        h.SeedInvited(session.SessionId, h.InstructorUserId, LiveParticipantRole.Instructor, invitedAgo);
        return (h, session, learnerId, learnerEmail, instructorEmail);
    }

    // ---- 24 hours -----------------------------------------------------------------------------------

    [Fact]
    public async Task TwentyFourHourReminder_GoesToTheLearnerAndTheInstructor_WithARequestCalendar()
    {
        var (h, session, learnerId, learnerEmail, instructorEmail) = Arrange(TimeSpan.FromHours(23), TimeSpan.FromDays(3));

        var result = await h.Service().SendRemindersAsync(Ct);

        var learnerMail = Assert.Single(h.Db.EmailsTo(learnerEmail));
        Assert.Equal("live-reminder-24h", learnerMail.TemplateKey);
        Assert.Equal("พรุ่งนี้มีเรียนสด: คาบเรียนสด — คอร์สทดสอบ", learnerMail.Subject);
        Assert.Equal("REQUEST", learnerMail.CalendarMethod);
        Assert.Contains("METHOD:REQUEST", learnerMail.IcsLines);
        Assert.Contains($"UID:{session.SessionId:N}@app.example.test", learnerMail.IcsLines);
        Assert.Contains("SEQUENCE:2", learnerMail.IcsLines); // the invite's own sequence — clients de-duplicate by UID + SEQUENCE
        Assert.Contains($"{InviteHarness.PublicBaseUrl}/live/{session.SessionId:D}/join", learnerMail.BodyHtml, StringComparison.Ordinal);
        Assert.Contains("ตั้งแต่เวลา", learnerMail.BodyHtml, StringComparison.Ordinal);

        var instructorMail = Assert.Single(h.Db.EmailsTo(instructorEmail));
        Assert.Equal("live-reminder-24h", instructorMail.TemplateKey);
        Assert.Equal("REQUEST", instructorMail.CalendarMethod);
        Assert.Contains($"{InviteHarness.PublicBaseUrl}/instructor/sessions/{session.SessionId:D}", instructorMail.BodyHtml, StringComparison.Ordinal);
        Assert.Contains("ห้องประชุมพร้อมใช้งานแล้ว", instructorMail.BodyHtml, StringComparison.Ordinal);

        Assert.All(h.Db.Notifications, n => Assert.Equal("live.reminder_24h", n.Type));
        Assert.Equal(2, h.Db.Notifications.Count);
        Assert.NotNull(h.Db.InviteFor(session.SessionId, learnerId)!.REMINDER_24H_SENT_AT_UTC);
        Assert.NotNull(h.Db.InviteFor(session.SessionId, h.InstructorUserId)!.REMINDER_24H_SENT_AT_UTC);
        Assert.Equal(2, result.RemindersSent);
        Assert.Equal(0, result.UnitsFailed);
    }

    [Fact]
    public async Task Reminders_RunThreeTimes_NeverSendTheSameReminderTwice()
    {
        var (h, _, _, _, _) = Arrange(TimeSpan.FromHours(23), TimeSpan.FromDays(3));

        await h.Service().SendRemindersAsync(Ct);
        var emails = h.Db.Emails.Count;
        var notifications = h.Db.Notifications.Count;
        var second = await h.Service().SendRemindersAsync(Ct);
        var third = await h.Service().SendRemindersAsync(Ct);

        Assert.Equal(2, emails);
        Assert.Equal(emails, h.Db.Emails.Count);
        Assert.Equal(notifications, h.Db.Notifications.Count);
        Assert.Equal(0, second.RemindersSent);
        Assert.Equal(0, third.RemindersSent);
    }

    [Fact]
    public async Task TwentyFourHourReminder_ExactlyAtTheBoundary_IsSent_AndAMomentEarlierIsNot()
    {
        var (atBoundary, _, _, _, _) = Arrange(TimeSpan.FromHours(24), TimeSpan.FromDays(3));
        await atBoundary.Service().SendRemindersAsync(Ct);
        Assert.Equal(2, atBoundary.Db.Emails.Count);

        var (early, _, _, _, _) = Arrange(TimeSpan.FromHours(24) + TimeSpan.FromSeconds(1), TimeSpan.FromDays(3));
        await early.Service().SendRemindersAsync(Ct);
        Assert.Empty(early.Db.Emails);
    }

    [Fact]
    public async Task Reminders_ASessionBeyondTheHorizon_IsNotEvenConsidered()
    {
        var (h, _, _, _, _) = Arrange(TimeSpan.FromHours(26), TimeSpan.FromDays(3));

        var result = await h.Service().SendRemindersAsync(Ct);

        Assert.Empty(h.Db.Emails);
        Assert.Equal(0, result.RemindersSent);
    }

    [Fact]
    public async Task TwentyFourHourReminder_IsSkipped_ForAnInviteSentInsideTheLast24Hours_ButTheOneHourReminderStillComes()
    {
        var (h, session, learnerId, learnerEmail, _) = Arrange(TimeSpan.FromHours(20), TimeSpan.FromHours(2));

        await h.Service().SendRemindersAsync(Ct);
        Assert.Empty(h.Db.Emails); // the invitation (2 h ago) already told them

        h.Clock.UtcNow = h.Clock.UtcNow.AddHours(19).AddMinutes(10); // 50 minutes to go
        await h.Service().SendRemindersAsync(Ct);

        var mail = Assert.Single(h.Db.EmailsTo(learnerEmail));
        Assert.Equal("live-reminder-1h", mail.TemplateKey);
        Assert.NotNull(h.Db.InviteFor(session.SessionId, learnerId)!.REMINDER_1H_SENT_AT_UTC);
        Assert.Null(h.Db.InviteFor(session.SessionId, learnerId)!.REMINDER_24H_SENT_AT_UTC);
    }

    // ---- 1 hour ---------------------------------------------------------------------------------------

    [Fact]
    public async Task OneHourReminder_CarriesNoCalendar_AndIsSentOnce()
    {
        var (h, session, learnerId, learnerEmail, instructorEmail) = Arrange(TimeSpan.FromMinutes(50), TimeSpan.FromDays(3));

        await h.Service().SendRemindersAsync(Ct);
        await h.Service().SendRemindersAsync(Ct);

        foreach (var address in new[] { learnerEmail, instructorEmail })
        {
            var mail = Assert.Single(h.Db.EmailsTo(address));
            Assert.Equal("live-reminder-1h", mail.TemplateKey);
            Assert.Null(mail.CalendarMethod);
            Assert.Null(mail.CalendarIcs);
        }

        Assert.Equal("อีก 1 ชั่วโมงเริ่มเรียนสด: คาบเรียนสด", h.Db.EmailsTo(learnerEmail).Single().Subject);
        Assert.All(h.Db.Notifications, n => Assert.Equal("live.reminder_1h", n.Type));
        Assert.NotNull(h.Db.InviteFor(session.SessionId, learnerId)!.REMINDER_1H_SENT_AT_UTC);
    }

    [Fact]
    public async Task BothReminders_AreSentAcrossTheDay_EachOnce()
    {
        var (h, session, learnerId, learnerEmail, _) = Arrange(TimeSpan.FromHours(23), TimeSpan.FromDays(3));

        await h.Service().SendRemindersAsync(Ct); // 23 h before -> 24 h reminder
        h.Clock.UtcNow = h.Clock.UtcNow.AddHours(22).AddMinutes(30); // 30 minutes before -> 1 h reminder
        await h.Service().SendRemindersAsync(Ct);
        await h.Service().SendRemindersAsync(Ct);

        var keys = h.Db.EmailsTo(learnerEmail).Select(e => e.TemplateKey).ToList();
        Assert.Equal(["live-reminder-24h", "live-reminder-1h"], keys);
        var invite = h.Db.InviteFor(session.SessionId, learnerId)!;
        Assert.NotNull(invite.REMINDER_24H_SENT_AT_UTC);
        Assert.NotNull(invite.REMINDER_1H_SENT_AT_UTC);
    }

    // ---- Instructor with a Google event -----------------------------------------------------------------

    [Fact]
    public async Task InstructorWhoseRoomIsAGoogleEvent_GetsTheReminder_ButNoCalendarFile()
    {
        var (h, _, _, learnerEmail, instructorEmail) = Arrange(TimeSpan.FromHours(23), TimeSpan.FromDays(3), googleRoom: true);

        await h.Service().SendRemindersAsync(Ct);

        var instructorMail = Assert.Single(h.Db.EmailsTo(instructorEmail));
        Assert.Equal("live-reminder-24h", instructorMail.TemplateKey);
        Assert.Null(instructorMail.CalendarIcs);
        Assert.Equal("REQUEST", Assert.Single(h.Db.EmailsTo(learnerEmail)).CalendarMethod); // learners always get theirs
    }

    // ---- Not due ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task NoReminders_ForACancelledSession_OrAnInviteThatWasWithdrawn()
    {
        var (h, session, learnerId, _, _) = Arrange(TimeSpan.FromHours(23), TimeSpan.FromDays(3));
        h.UpdateSession(session.SessionId, s => s with { Status = LiveSessionStatus.Cancelled });

        await h.Service().SendRemindersAsync(Ct);
        Assert.Empty(h.Db.Emails);

        var (h2, session2, learner2, _, _) = Arrange(TimeSpan.FromHours(23), TimeSpan.FromDays(3));
        h2.ChangeInvite(session2.SessionId, learner2, i => i.MarkCancelled(3, h2.Clock));
        await h2.Service().SendRemindersAsync(Ct);
        Assert.DoesNotContain(h2.Db.Emails, e => e.Subject.Contains("พรุ่งนี้", StringComparison.Ordinal) && e.To.StartsWith("learner-", StringComparison.Ordinal));
        _ = learnerId;
    }

    [Fact]
    public async Task APendingInvite_IsNeverReminded()
    {
        var h = new InviteHarness();
        var courseId = Guid.NewGuid();
        var session = h.AddSession(courseId, TimeSpan.FromHours(23));
        var (learnerId, _) = h.AddLearner(courseId);
        h.Db.Seed(SESSION_INVITE.Create(session.SessionId, learnerId, LiveParticipantRole.Learner, h.Clock));

        await h.Service().SendRemindersAsync(Ct);

        Assert.Empty(h.Db.Emails);
    }

    [Fact]
    public async Task ARescheduledInvite_WhoseRemindersWereReset_IsRemindedAgainAtTheNewTime()
    {
        var (h, session, learnerId, learnerEmail, _) = Arrange(TimeSpan.FromHours(23), TimeSpan.FromDays(3));
        await h.Service().SendRemindersAsync(Ct);
        Assert.Single(h.Db.EmailsTo(learnerEmail));

        // the reconcile moved the session a day later and reset the reminders
        h.UpdateSession(session.SessionId, s => s with { StartsAtUtc = s.StartsAtUtc.AddDays(1), EndsAtUtc = s.EndsAtUtc.AddDays(1) });
        h.ChangeInvite(session.SessionId, learnerId, i => i.ResetReminders());
        h.Clock.UtcNow = h.Clock.UtcNow.AddHours(1); // now 46 h before the new start -> not due yet
        await h.Service().SendRemindersAsync(Ct);
        Assert.Single(h.Db.EmailsTo(learnerEmail));

        h.Clock.UtcNow = h.Clock.UtcNow.AddHours(23); // 23 h before the new start
        await h.Service().SendRemindersAsync(Ct);
        Assert.Equal(2, h.Db.EmailsTo(learnerEmail).Count());
    }

    // ---- Contacts and failures -----------------------------------------------------------------------------------

    [Fact]
    public async Task ARecipientWithNoAddress_IsSkipped_WithoutBlockingOrThrowing()
    {
        var (h, session, learnerId, learnerEmail, instructorEmail) = Arrange(TimeSpan.FromHours(23), TimeSpan.FromDays(3));
        h.Contacts.Users.Remove(learnerId);

        var result = await h.Service().SendRemindersAsync(Ct);

        Assert.Empty(h.Db.EmailsTo(learnerEmail));
        Assert.Single(h.Db.EmailsTo(instructorEmail));
        Assert.Equal(1, result.RemindersSent);
        Assert.Equal(1, result.Skipped);
        var invite = h.Db.InviteFor(session.SessionId, learnerId)!;
        Assert.Equal(InviteStatus.Skipped, invite.STATUS);
        Assert.Equal("no_contact", invite.ERROR);
    }

    [Fact]
    public async Task AFailedSlice_RollsBackWhole_OtherSlicesStillSend_AndTheNextRunCompletesIt()
    {
        var h = new InviteHarness();
        var recipients = new List<string>();
        // 12 sessions => two slices of 10 and 2 (ordered by start time)
        for (var i = 0; i < 12; i++)
        {
            var courseId = Guid.NewGuid();
            var session = h.AddSession(courseId, TimeSpan.FromHours(10) + TimeSpan.FromMinutes(i));
            var (learnerId, learnerEmail) = h.AddLearner(courseId);
            h.SeedInvited(session.SessionId, learnerId, LiveParticipantRole.Learner, TimeSpan.FromDays(3));
            recipients.Add(learnerEmail);
        }

        h.Db.ThrowOnNextSave = new InvalidOperationException("db down"); // the first slice's save fails

        var first = await h.Service().SendRemindersAsync(Ct);

        Assert.Equal(1, first.UnitsFailed);
        Assert.Equal(2, first.RemindersSent); // the second slice
        Assert.Equal(2, h.Db.Emails.Count);
        Assert.Equal(2, h.Db.Notifications.Count);
        Assert.Equal(2, h.Db.Invites.Count(i => i.REMINDER_24H_SENT_AT_UTC is not null)); // the failed slice's flags were NOT saved

        var second = await h.Service().SendRemindersAsync(Ct);

        Assert.Equal(0, second.UnitsFailed);
        Assert.Equal(10, second.RemindersSent);
        Assert.Equal(12, h.Db.Emails.Count);
        Assert.Equal(recipients.Order(), h.Db.Emails.Select(e => e.To).Order()); // each exactly once
    }

    // ---- Room not ready ---------------------------------------------------------------------------------------------

    [Fact]
    public async Task RoomNotReady_WithinADay_WarnsTheInstructorOnce_WithItsOwnFlag()
    {
        var h = new InviteHarness();
        var instructorEmail = h.AddInstructorContact();
        var courseId = Guid.NewGuid();
        var session = h.AddSession(courseId, TimeSpan.FromHours(20), "คาบที่ยังไม่มีห้อง");
        var meeting = h.AddAwaitingLinkMeeting(session.SessionId);
        // the "paste a link" alert already went out days ago and consumed the other flag
        h.ChangeMeeting(session.SessionId, m => m.MarkAlertSent(new FakeClock(h.Clock.UtcNow.AddDays(-4))));
        var alertStamp = h.Db.Meetings.Single().MEETING_ALERT_SENT_AT_UTC;
        _ = meeting;

        var result = await h.Service().SendRemindersAsync(Ct);

        var email = Assert.Single(h.Db.EmailsTo(instructorEmail));
        Assert.Equal("live-meeting-alert", email.TemplateKey);
        Assert.Equal("ห้องประชุมของคาบ คาบที่ยังไม่มีห้อง ยังไม่พร้อม", email.Subject);
        Assert.Null(email.CalendarIcs);
        Assert.Contains($"{InviteHarness.PublicBaseUrl}/instructor/sessions/{session.SessionId:D}", email.BodyHtml, StringComparison.Ordinal);

        var inApp = Assert.Single(h.Db.Notifications);
        Assert.Equal("live.meeting_alert", inApp.Type);
        Assert.Equal(h.InstructorUserId, inApp.UserId);
        Assert.Equal($"/instructor/sessions/{session.SessionId:D}", inApp.LinkUrl);

        var stored = h.Db.Meetings.Single();
        Assert.NotNull(stored.READINESS_ALERT_SENT_AT_UTC);
        Assert.Equal(alertStamp, stored.MEETING_ALERT_SENT_AT_UTC); // the other flag is untouched
        Assert.Equal(1, result.InstructorAlertsSent);

        await h.Service().SendRemindersAsync(Ct);
        Assert.Single(h.Db.EmailsTo(instructorEmail)); // once
    }

    [Fact]
    public async Task RoomNotReady_NothingForAUsableRoom_ASessionFurtherThanADay_OrAMissingMeetingRow()
    {
        var h = new InviteHarness();
        var instructorEmail = h.AddInstructorContact();
        var usable = h.AddSession(Guid.NewGuid(), TimeSpan.FromHours(20));
        h.AddManualMeeting(usable.SessionId);
        var far = h.AddSession(Guid.NewGuid(), TimeSpan.FromHours(24) + TimeSpan.FromMinutes(1));
        h.AddAwaitingLinkMeeting(far.SessionId);
        h.AddSession(Guid.NewGuid(), TimeSpan.FromHours(10)); // no meeting row at all (the sync job adopts it soon)

        await h.Service().SendRemindersAsync(Ct);

        Assert.Empty(h.Db.EmailsTo(instructorEmail));
        Assert.Empty(h.Db.Notifications);
    }

    [Fact]
    public async Task RoomNotReady_AnInstructorWithoutAnAddress_StillGetsTheInAppNotification()
    {
        var h = new InviteHarness();
        var session = h.AddSession(Guid.NewGuid(), TimeSpan.FromHours(10));
        h.AddAwaitingLinkMeeting(session.SessionId);

        await h.Service().SendRemindersAsync(Ct);

        Assert.Empty(h.Db.Emails);
        Assert.Equal("live.meeting_alert", Assert.Single(h.Db.Notifications).Type);
        Assert.NotNull(h.Db.Meetings.Single().READINESS_ALERT_SENT_AT_UTC);
    }

    [Fact]
    public async Task RoomNotReady_AFailedSave_CommitsNothing_AndTheNextRunAlertsAgain()
    {
        var h = new InviteHarness();
        var instructorEmail = h.AddInstructorContact();
        var session = h.AddSession(Guid.NewGuid(), TimeSpan.FromHours(10));
        h.AddAwaitingLinkMeeting(session.SessionId);
        h.Db.ThrowOnNextSave = new InvalidOperationException("db down");

        var failed = await h.Service().SendRemindersAsync(Ct);

        Assert.Equal(1, failed.UnitsFailed);
        Assert.Empty(h.Db.Emails);
        Assert.Empty(h.Db.Notifications);
        Assert.Null(h.Db.Meetings.Single().READINESS_ALERT_SENT_AT_UTC);

        var ok = await h.Service().SendRemindersAsync(Ct);

        Assert.Equal(1, ok.InstructorAlertsSent);
        Assert.Single(h.Db.EmailsTo(instructorEmail));
    }

    [Fact]
    public async Task Reminders_NothingScheduled_DoesNothing()
    {
        var h = new InviteHarness();

        var result = await h.Service().SendRemindersAsync(Ct);

        Assert.Equal(new ReminderRunResult(0, 0, 0, 0), result);
    }
}
