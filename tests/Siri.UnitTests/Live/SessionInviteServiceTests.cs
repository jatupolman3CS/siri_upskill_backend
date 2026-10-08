using System.Text.RegularExpressions;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;

namespace Siri.UnitTests.Live;

/// <summary>
/// The invite reconcile (docs/contracts/P11-04-live-invites-ics-reminders.md §4.4 and the §5 checklist) driven against in-memory fakes
/// that emulate one shared unit of work — so the all-or-nothing and idempotency properties are tested for real, not assumed.
/// </summary>
public class SessionInviteServiceTests
{
    private static readonly CancellationToken Ct = CancellationToken.None;

    private static InviteHarness NewHarness() => new();

    // ---- 1. Purchase day ---------------------------------------------------------------------------

    [Fact]
    public async Task Reconcile_PurchaseDay_SendsOneBatchEmail_WithAPublishCalendarHoldingEveryFutureSession()
    {
        var h = NewHarness();
        h.AddInstructorContact();
        var courseId = Guid.NewGuid();
        var sessions = Enumerable.Range(1, 5).Select(day => h.AddSession(courseId, TimeSpan.FromDays(day), $"คาบที่ {day}")).ToList();
        var (learnerId, learnerEmail) = h.AddLearner(courseId);

        var result = await h.Service().ReconcileAsync(Ct);

        var email = Assert.Single(h.Db.EmailsTo(learnerEmail));
        Assert.Equal("live-invite-batch", email.TemplateKey);
        Assert.Equal("ยืนยันตารางเรียนสด: คอร์สทดสอบ", email.Subject);
        Assert.Equal("PUBLISH", email.CalendarMethod);
        Assert.Contains("METHOD:PUBLISH", email.IcsLines);
        Assert.Equal(5, email.EventCount);
        foreach (var session in sessions)
        {
            Assert.Contains($"UID:{session.SessionId:N}@app.example.test", email.IcsLines);
            Assert.Contains($"{InviteHarness.PublicBaseUrl}/live/{session.SessionId:D}/join", email.BodyHtml, StringComparison.Ordinal);
        }

        Assert.All(email.Property("SEQUENCE"), line => Assert.Equal("SEQUENCE:0", line));
        Assert.Empty(email.Property("ATTENDEE"));

        var notification = Assert.Single(h.Db.Notifications, n => n.UserId == learnerId);
        Assert.Equal("live.invite", notification.Type);
        Assert.Equal("/learn/test-course?tab=live", notification.LinkUrl);

        Assert.All(sessions, s =>
        {
            var invite = h.Db.InviteFor(s.SessionId, learnerId);
            Assert.NotNull(invite);
            Assert.Equal(InviteStatus.Invited, invite.STATUS);
            Assert.Equal(0, invite.ICS_SEQUENCE_SENT);
            Assert.NotNull(invite.INVITE_SENT_AT_UTC);
        });

        Assert.Equal(1, result.CoursesProcessed);
        Assert.Equal(1, result.EmailsStaged);
        Assert.Equal(0, result.CoursesFailed);
    }

    [Fact]
    public async Task Reconcile_RunThreeTimesInARow_StagesNothingTheSecondAndThirdTime()
    {
        var h = NewHarness();
        h.AddInstructorContact();
        var courseId = Guid.NewGuid();
        h.AddSession(courseId, TimeSpan.FromDays(1));
        h.AddSession(courseId, TimeSpan.FromDays(2));
        h.AddLearner(courseId);
        h.AddLearner(courseId);

        await h.Service().ReconcileAsync(Ct);
        var emails = h.Db.Emails.Count;
        var notifications = h.Db.Notifications.Count;
        var invites = h.Db.Invites.Count;
        Assert.Equal(2, emails);

        var second = await h.Service().ReconcileAsync(Ct);
        var third = await h.Service().ReconcileAsync(Ct);

        Assert.Equal(emails, h.Db.Emails.Count);
        Assert.Equal(notifications, h.Db.Notifications.Count);
        Assert.Equal(invites, h.Db.Invites.Count);
        Assert.Equal(0, second.EmailsStaged);
        Assert.Equal(0, third.EmailsStaged);
    }

    [Fact]
    public async Task Reconcile_ASessionThatStartedJustNow_IsNotInvitedTo()
    {
        var h = NewHarness();
        var courseId = Guid.NewGuid();
        var started = h.AddSession(courseId, TimeSpan.Zero); // starts exactly now
        var (learnerId, _) = h.AddLearner(courseId);

        await h.Service().ReconcileAsync(Ct);

        Assert.Null(h.Db.InviteFor(started.SessionId, learnerId));
        Assert.Empty(h.Db.Emails);
    }

    // ---- 2. Latecomer -------------------------------------------------------------------------------

    [Fact]
    public async Task Reconcile_Latecomer_IsInvitedOnlyToFutureSessions_NeverToPastOnes()
    {
        var h = NewHarness();
        var courseId = Guid.NewGuid();
        var ended = h.AddSession(courseId, TimeSpan.FromDays(-2), "จบไปแล้ว");
        var running = h.AddSession(courseId, TimeSpan.FromMinutes(-10), "กำลังสอน");
        var future1 = h.AddSession(courseId, TimeSpan.FromDays(1), "อนาคต 1");
        var future2 = h.AddSession(courseId, TimeSpan.FromDays(2), "อนาคต 2");
        var (learnerId, learnerEmail) = h.AddLearner(courseId);

        await h.Service().ReconcileAsync(Ct);

        Assert.Null(h.Db.InviteFor(ended.SessionId, learnerId));
        Assert.Null(h.Db.InviteFor(running.SessionId, learnerId));
        Assert.NotNull(h.Db.InviteFor(future1.SessionId, learnerId));
        Assert.NotNull(h.Db.InviteFor(future2.SessionId, learnerId));

        var email = Assert.Single(h.Db.EmailsTo(learnerEmail));
        Assert.Equal(2, email.EventCount);
        Assert.DoesNotContain($"{ended.SessionId:N}", email.CalendarIcs, StringComparison.Ordinal);
        Assert.DoesNotContain($"{running.SessionId:N}", email.CalendarIcs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reconcile_ANewSessionAddedLater_SendsASecondEmail_WithOnlyTheNewSession()
    {
        var h = NewHarness();
        var courseId = Guid.NewGuid();
        var first = h.AddSession(courseId, TimeSpan.FromDays(1));
        var (learnerId, learnerEmail) = h.AddLearner(courseId);
        await h.Service().ReconcileAsync(Ct);

        var added = h.AddSession(courseId, TimeSpan.FromDays(3), "คาบใหม่");
        await h.Service().ReconcileAsync(Ct);

        var emails = h.Db.EmailsTo(learnerEmail).ToList();
        Assert.Equal(2, emails.Count);
        var second = emails[1];
        Assert.Equal("เพิ่มคาบเรียนสดใหม่: คอร์สทดสอบ", second.Subject);
        Assert.Equal(1, second.EventCount);
        Assert.Contains($"UID:{added.SessionId:N}@app.example.test", second.IcsLines);
        Assert.DoesNotContain($"{first.SessionId:N}", second.CalendarIcs, StringComparison.Ordinal);
        Assert.NotNull(h.Db.InviteFor(added.SessionId, learnerId));
    }

    // ---- 3. Revoke / expire --------------------------------------------------------------------------

    [Fact]
    public async Task Reconcile_RevokedEnrollment_SendsACancelCalendar_WithTheNextSequence_AndOnlyOnce()
    {
        var h = NewHarness();
        var courseId = Guid.NewGuid();
        var s1 = h.AddSession(courseId, TimeSpan.FromDays(1));
        var s2 = h.AddSession(courseId, TimeSpan.FromDays(2));
        var (learnerId, learnerEmail) = h.AddLearner(courseId);
        await h.Service().ReconcileAsync(Ct);

        h.Unenroll(courseId, learnerId);
        await h.Service().ReconcileAsync(Ct);

        var lapsed = Assert.Single(h.Db.EmailsTo(learnerEmail), e => e.TemplateKey == "live-invite-lapsed");
        Assert.Equal("CANCEL", lapsed.CalendarMethod);
        Assert.Contains("METHOD:CANCEL", lapsed.IcsLines);
        Assert.Equal(2, lapsed.EventCount);
        Assert.All(lapsed.Property("STATUS"), line => Assert.Equal("STATUS:CANCELLED", line));
        Assert.All(lapsed.Property("SEQUENCE"), line => Assert.Equal("SEQUENCE:1", line)); // sent 0 -> cancel 1
        Assert.Equal(2, lapsed.Property("ATTENDEE").Count);
        Assert.All(lapsed.Property("ATTENDEE"), line => Assert.EndsWith($":mailto:{learnerEmail}", line, StringComparison.Ordinal));
        Assert.DoesNotContain("คืนเงิน", lapsed.BodyHtml, StringComparison.Ordinal);

        foreach (var s in new[] { s1, s2 })
        {
            var invite = h.Db.InviteFor(s.SessionId, learnerId)!;
            Assert.Equal(InviteStatus.Cancelled, invite.STATUS);
            Assert.Equal(1, invite.ICS_SEQUENCE_SENT);
            Assert.NotNull(invite.CANCEL_SENT_AT_UTC);
        }

        var emailCount = h.Db.Emails.Count;
        await h.Service().ReconcileAsync(Ct);
        Assert.Equal(emailCount, h.Db.Emails.Count); // idempotent
    }

    [Fact]
    public async Task Reconcile_RepurchaseAfterARevoke_SendsAFreshInvite_WithAHigherSequenceThanTheCancel()
    {
        var h = NewHarness();
        var courseId = Guid.NewGuid();
        var session = h.AddSession(courseId, TimeSpan.FromDays(2));
        var (learnerId, learnerEmail) = h.AddLearner(courseId);
        await h.Service().ReconcileAsync(Ct);
        h.Unenroll(courseId, learnerId);
        await h.Service().ReconcileAsync(Ct);

        h.Enroll(courseId, learnerId);
        await h.Service().ReconcileAsync(Ct);

        var emails = h.Db.EmailsTo(learnerEmail).ToList();
        Assert.Equal(3, emails.Count); // invite, lapsed, invite again
        var again = emails[2];
        Assert.Equal("live-invite-batch", again.TemplateKey);
        Assert.Equal("ยืนยันตารางเรียนสด: คอร์สทดสอบ", again.Subject); // bought again => first invitation of this enrollment
        Assert.Equal("PUBLISH", again.CalendarMethod);
        Assert.All(again.Property("SEQUENCE"), line => Assert.Equal("SEQUENCE:2", line)); // 0 invite, 1 cancel, 2 re-invite

        var invite = h.Db.InviteFor(session.SessionId, learnerId)!;
        Assert.Equal(InviteStatus.Invited, invite.STATUS);
        Assert.Equal(2, invite.ICS_SEQUENCE_SENT);
        Assert.Null(invite.CANCEL_SENT_AT_UTC);
    }

    [Fact]
    public async Task Reconcile_AnInviteThatWasNeverSentToSomeoneWhoLostAccess_IsDroppedSilently()
    {
        var h = NewHarness();
        var courseId = Guid.NewGuid();
        var session = h.AddSession(courseId, TimeSpan.FromDays(1));
        h.AddLearner(courseId); // someone else, so the course is processed
        var strangerId = Guid.NewGuid();
        h.Db.Seed(SESSION_INVITE.Create(session.SessionId, strangerId, LiveParticipantRole.Learner, h.Clock));

        await h.Service().ReconcileAsync(Ct);

        var invite = h.Db.InviteFor(session.SessionId, strangerId)!;
        Assert.Equal(InviteStatus.Cancelled, invite.STATUS);
        Assert.DoesNotContain(h.Db.Emails, e => e.TemplateKey == "live-invite-lapsed");
    }

    [Fact]
    public async Task Reconcile_ALapsedLearnerWithNoUsableAddress_IsStillWithdrawn_WithoutAnEmail()
    {
        var h = NewHarness();
        var courseId = Guid.NewGuid();
        var session = h.AddSession(courseId, TimeSpan.FromDays(1));
        var (learnerId, _) = h.AddLearner(courseId);
        await h.Service().ReconcileAsync(Ct);
        h.Unenroll(courseId, learnerId);
        h.Contacts.Users.Remove(learnerId);
        var emailsBefore = h.Db.Emails.Count;

        await h.Service().ReconcileAsync(Ct);

        Assert.Equal(emailsBefore, h.Db.Emails.Count);
        Assert.Equal(InviteStatus.Cancelled, h.Db.InviteFor(session.SessionId, learnerId)!.STATUS);
    }

    // ---- 4. Cancelled session ---------------------------------------------------------------------------

    [Fact]
    public async Task Reconcile_ACancelledSession_SendsEachInvitedLearnerOneCancel_AndNeverAgain()
    {
        var h = NewHarness();
        var courseId = Guid.NewGuid();
        var kept = h.AddSession(courseId, TimeSpan.FromDays(1), "คาบที่คงอยู่");
        var cancelled = h.AddSession(courseId, TimeSpan.FromDays(2), "คาบที่ยกเลิก");
        h.AddManualMeeting(kept.SessionId);
        h.AddManualMeeting(cancelled.SessionId);
        var learners = Enumerable.Range(0, 3).Select(_ => h.AddLearner(courseId)).ToList();
        await h.Service().ReconcileAsync(Ct);
        var emailsBefore = h.Db.Emails.Count;

        h.UpdateSession(cancelled.SessionId, s => s with { Status = LiveSessionStatus.Cancelled, CancelReason = "ผู้สอนป่วย" });
        h.ChangeMeeting(cancelled.SessionId, m => m.MarkSessionCancelled());
        await h.Service().ReconcileAsync(Ct);

        foreach (var (learnerId, learnerEmail) in learners)
        {
            var cancel = Assert.Single(h.Db.EmailsTo(learnerEmail), e => e.TemplateKey == "live-session-cancelled");
            Assert.Equal("CANCEL", cancel.CalendarMethod);
            Assert.Equal("ยกเลิกคาบเรียนสด: คาบที่ยกเลิก — คอร์สทดสอบ", cancel.Subject);
            Assert.Contains("ผู้สอนป่วย", cancel.BodyHtml, StringComparison.Ordinal);
            Assert.Contains($"UID:{cancelled.SessionId:N}@app.example.test", cancel.IcsLines);
            Assert.Contains("STATUS:CANCELLED", cancel.IcsLines);
            Assert.Contains("SEQUENCE:1", cancel.IcsLines);
            Assert.Equal(InviteStatus.Cancelled, h.Db.InviteFor(cancelled.SessionId, learnerId)!.STATUS);
            Assert.Equal(InviteStatus.Invited, h.Db.InviteFor(kept.SessionId, learnerId)!.STATUS);
        }

        Assert.Equal(emailsBefore + 3, h.Db.Emails.Count);
        await h.Service().ReconcileAsync(Ct);
        Assert.Equal(emailsBefore + 3, h.Db.Emails.Count);
    }

    [Fact]
    public async Task Reconcile_ACancelledSession_DropsInvitesThatWereNeverSent_Silently()
    {
        var h = NewHarness();
        var courseId = Guid.NewGuid();
        var session = h.AddSession(courseId, TimeSpan.FromDays(1));
        var (learnerId, _) = h.AddLearner(courseId);
        h.Db.Seed(SESSION_INVITE.Create(session.SessionId, learnerId, LiveParticipantRole.Learner, h.Clock)); // pending, never sent
        h.UpdateSession(session.SessionId, s => s with { Status = LiveSessionStatus.Cancelled });

        await h.Service().ReconcileAsync(Ct);

        Assert.Equal(InviteStatus.Cancelled, h.Db.InviteFor(session.SessionId, learnerId)!.STATUS);
        Assert.Empty(h.Db.Emails);
    }

    // ---- 5. Reschedule ------------------------------------------------------------------------------------

    [Fact]
    public async Task Reconcile_ARescheduledSession_SendsARequestWithTheBumpedSequence_AndRestartsTheReminders()
    {
        var h = NewHarness();
        var courseId = Guid.NewGuid();
        var session = h.AddSession(courseId, TimeSpan.FromDays(3));
        h.AddManualMeeting(session.SessionId);
        var (learnerId, learnerEmail) = h.AddLearner(courseId);
        await h.Service().ReconcileAsync(Ct);
        // pretend the reminders for the old time were already sent
        h.ChangeInvite(session.SessionId, learnerId, i =>
        {
            i.MarkReminder24h(h.Clock);
            i.MarkReminder1h(h.Clock);
        });

        var moved = h.UpdateSession(session.SessionId, s => s with { StartsAtUtc = s.StartsAtUtc.AddDays(2), EndsAtUtc = s.EndsAtUtc.AddDays(2) });
        h.ChangeMeeting(session.SessionId, m => m.MarkSessionChanged());
        await h.Service().ReconcileAsync(Ct);

        var update = Assert.Single(h.Db.EmailsTo(learnerEmail), e => e.TemplateKey == "live-session-updated");
        Assert.Equal("REQUEST", update.CalendarMethod);
        Assert.Contains("METHOD:REQUEST", update.IcsLines);
        Assert.Contains("SEQUENCE:1", update.IcsLines);
        Assert.Contains($"UID:{session.SessionId:N}@app.example.test", update.IcsLines);
        Assert.Contains($"DTSTART:{moved.StartsAtUtc:yyyyMMdd'T'HHmmss'Z'}", update.IcsLines);
        Assert.Single(update.Property("ATTENDEE"));
        Assert.Contains(ThaiDateText.Format(moved.StartsAtUtc, moved.EndsAtUtc), update.BodyHtml, StringComparison.Ordinal);

        var invite = h.Db.InviteFor(session.SessionId, learnerId)!;
        Assert.Equal(1, invite.ICS_SEQUENCE_SENT);
        Assert.Null(invite.REMINDER_24H_SENT_AT_UTC); // restarted — due again relative to the new time
        Assert.Null(invite.REMINDER_1H_SENT_AT_UTC);

        var emailCount = h.Db.Emails.Count;
        await h.Service().ReconcileAsync(Ct);
        Assert.Equal(emailCount, h.Db.Emails.Count); // the update is sent once
    }

    [Fact]
    public async Task Reconcile_ASessionMovedCloserThanADay_DoesNotAlsoGetATomorrowReminderRightAfterTheUpdate()
    {
        var h = NewHarness();
        var courseId = Guid.NewGuid();
        var session = h.AddSession(courseId, TimeSpan.FromDays(4));
        h.AddManualMeeting(session.SessionId);
        var (learnerId, _) = h.AddLearner(courseId);
        await h.Service().ReconcileAsync(Ct);

        h.UpdateSession(session.SessionId, s => s with { StartsAtUtc = h.Clock.UtcNow.AddHours(10), EndsAtUtc = h.Clock.UtcNow.AddHours(12) });
        h.ChangeMeeting(session.SessionId, m => m.MarkSessionChanged());
        await h.Service().ReconcileAsync(Ct);

        var invite = h.Db.InviteFor(session.SessionId, learnerId)!;
        Assert.NotNull(invite.REMINDER_24H_SENT_AT_UTC); // the update e-mail was the notice for the 24 h moment that has passed
        Assert.Null(invite.REMINDER_1H_SENT_AT_UTC);
    }

    // ---- 6. Instructor ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Reconcile_InstructorBatchWaitsUntilTheRoomProviderIsDecided()
    {
        var h = NewHarness();
        var instructorEmail = h.AddInstructorContact();
        var courseId = Guid.NewGuid();
        var session = h.AddSession(courseId, TimeSpan.FromDays(1));
        h.AddLearner(courseId);

        await h.Service().ReconcileAsync(Ct);

        Assert.Empty(h.Db.EmailsTo(instructorEmail)); // no meeting row yet
        Assert.Equal(InviteStatus.Pending, h.Db.InviteFor(session.SessionId, h.InstructorUserId)!.STATUS);

        var staged = SESSION_MEETING.Stage(session.SessionId); // provider still undecided
        h.Db.Seed(staged);
        await h.Service().ReconcileAsync(Ct);
        Assert.Empty(h.Db.EmailsTo(instructorEmail));
    }

    [Fact]
    public async Task Reconcile_InstructorWithAManualRoom_GetsAnEmailWithAPublishCalendar_AndAnInAppNotification()
    {
        var h = NewHarness();
        var instructorEmail = h.AddInstructorContact();
        var courseId = Guid.NewGuid();
        var s1 = h.AddSession(courseId, TimeSpan.FromDays(1), "คาบที่ 1");
        var s2 = h.AddSession(courseId, TimeSpan.FromDays(2), "คาบที่ 2");
        h.AddManualMeeting(s1.SessionId);
        h.AddAwaitingLinkMeeting(s2.SessionId); // provider decided (Manual), no link yet

        await h.Service().ReconcileAsync(Ct);

        var email = Assert.Single(h.Db.EmailsTo(instructorEmail));
        Assert.Equal("live-instructor-batch", email.TemplateKey);
        Assert.Equal("สร้างตารางสอนสดแล้ว: คอร์สทดสอบ", email.Subject);
        Assert.Equal("PUBLISH", email.CalendarMethod);
        Assert.Equal(2, email.EventCount);
        Assert.Contains($"{InviteHarness.PublicBaseUrl}/instructor/sessions/{s1.SessionId:D}", email.BodyHtml, StringComparison.Ordinal);
        Assert.Contains("ห้องพร้อมใช้งาน", email.BodyHtml, StringComparison.Ordinal);
        Assert.Contains("ยังไม่มีห้องประชุม", email.BodyHtml, StringComparison.Ordinal);

        var notification = Assert.Single(h.Db.Notifications, n => n.UserId == h.InstructorUserId);
        Assert.Equal("live.invite", notification.Type);
        Assert.Equal($"/instructor/sessions/{s1.SessionId:D}", notification.LinkUrl);
        Assert.All([s1, s2], s => Assert.Equal(InviteStatus.Invited, h.Db.InviteFor(s.SessionId, h.InstructorUserId)!.STATUS));
    }

    [Fact]
    public async Task Reconcile_InstructorWithGoogleMeet_GetsTheEmail_ButNoCalendarFile()
    {
        var h = NewHarness();
        var instructorEmail = h.AddInstructorContact();
        var courseId = Guid.NewGuid();
        var session = h.AddSession(courseId, TimeSpan.FromDays(1));
        h.AddGoogleMeeting(session.SessionId);

        await h.Service().ReconcileAsync(Ct);

        var email = Assert.Single(h.Db.EmailsTo(instructorEmail));
        Assert.Equal("live-instructor-batch", email.TemplateKey);
        Assert.Null(email.CalendarMethod); // the event is already on their own Google calendar — a file would duplicate it
        Assert.Null(email.CalendarIcs);
    }

    [Fact]
    public async Task Reconcile_InstructorBatchWithMixedProviders_AttachesOnlyTheNonGoogleSessions()
    {
        var h = NewHarness();
        var instructorEmail = h.AddInstructorContact();
        var courseId = Guid.NewGuid();
        var manual = h.AddSession(courseId, TimeSpan.FromDays(1));
        var google = h.AddSession(courseId, TimeSpan.FromDays(2));
        h.AddManualMeeting(manual.SessionId);
        h.AddGoogleMeeting(google.SessionId);

        await h.Service().ReconcileAsync(Ct);

        var email = Assert.Single(h.Db.EmailsTo(instructorEmail));
        Assert.Equal(1, email.EventCount);
        Assert.Contains($"UID:{manual.SessionId:N}@app.example.test", email.IcsLines);
        Assert.DoesNotContain(google.SessionId.ToString("N"), email.CalendarIcs, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Reconcile_InstructorUpdateAndCancel_AreEmailedForAManualRoom_ButSilentForAGoogleEvent()
    {
        var h = NewHarness();
        var instructorEmail = h.AddInstructorContact();
        var courseId = Guid.NewGuid();
        var manual = h.AddSession(courseId, TimeSpan.FromDays(2), "คาบ manual");
        var google = h.AddSession(courseId, TimeSpan.FromDays(3), "คาบ google");
        h.AddManualMeeting(manual.SessionId);
        h.AddGoogleMeeting(google.SessionId);
        await h.Service().ReconcileAsync(Ct);
        var after = h.Db.EmailsTo(instructorEmail).Count();
        var inAppBefore = h.Db.Notifications.Count(n => n.UserId == h.InstructorUserId);

        // both sessions move, then both are cancelled
        foreach (var s in new[] { manual, google })
        {
            h.UpdateSession(s.SessionId, c => c with { StartsAtUtc = c.StartsAtUtc.AddHours(5), EndsAtUtc = c.EndsAtUtc.AddHours(5) });
            h.ChangeMeeting(s.SessionId, m => m.MarkSessionChanged());
        }

        await h.Service().ReconcileAsync(Ct);
        var updates = h.Db.EmailsTo(instructorEmail).Skip(after).ToList();
        var update = Assert.Single(updates);
        Assert.Equal("live-instructor-session-updated", update.TemplateKey);
        Assert.Equal("REQUEST", update.CalendarMethod);
        Assert.Contains($"UID:{manual.SessionId:N}@app.example.test", update.IcsLines);
        Assert.Equal(inAppBefore + 1, h.Db.Notifications.Count(n => n.UserId == h.InstructorUserId));
        // the Google event's invite still advanced, silently
        Assert.Equal(1, h.Db.InviteFor(google.SessionId, h.InstructorUserId)!.ICS_SEQUENCE_SENT);

        foreach (var s in new[] { manual, google })
        {
            h.UpdateSession(s.SessionId, c => c with { Status = LiveSessionStatus.Cancelled });
            h.ChangeMeeting(s.SessionId, m => m.MarkSessionCancelled());
        }

        var beforeCancel = h.Db.EmailsTo(instructorEmail).Count();
        await h.Service().ReconcileAsync(Ct);

        var cancels = h.Db.EmailsTo(instructorEmail).Skip(beforeCancel).ToList();
        var cancel = Assert.Single(cancels);
        Assert.Equal("live-instructor-session-cancelled", cancel.TemplateKey);
        Assert.Equal("CANCEL", cancel.CalendarMethod);
        Assert.Equal(InviteStatus.Cancelled, h.Db.InviteFor(google.SessionId, h.InstructorUserId)!.STATUS);
        Assert.Equal(InviteStatus.Cancelled, h.Db.InviteFor(manual.SessionId, h.InstructorUserId)!.STATUS);
    }

    [Fact]
    public async Task Reconcile_AnInstructorWhoIsAlsoEnrolled_IsOnlyEverAnInstructorParticipant()
    {
        var h = NewHarness();
        h.AddInstructorContact();
        var courseId = Guid.NewGuid();
        var session = h.AddSession(courseId, TimeSpan.FromDays(1));
        h.AddManualMeeting(session.SessionId);
        h.Enroll(courseId, h.InstructorUserId); // the owner holds an enrollment too

        await h.Service().ReconcileAsync(Ct);

        var invite = Assert.Single(h.Db.Invites);
        Assert.Equal(LiveParticipantRole.Instructor, invite.ROLE);
        var email = Assert.Single(h.Db.Emails);
        Assert.Equal("live-instructor-batch", email.TemplateKey);
    }

    // ---- 7. Contacts --------------------------------------------------------------------------------------------

    [Fact]
    public async Task Reconcile_ALearnerWithNoContactOrABrokenAddress_IsSkipped_WithoutBlockingTheOthers()
    {
        var h = NewHarness();
        var courseId = Guid.NewGuid();
        var session = h.AddSession(courseId, TimeSpan.FromDays(1));
        var (okId, okEmail) = h.AddLearner(courseId);
        var (missingId, _) = h.AddLearner(courseId);
        h.Contacts.Users.Remove(missingId);
        var (brokenId, _) = h.AddLearner(courseId);
        h.Contacts.Users[brokenId] = ("not an address\r\nBcc: x@example.test", "ชื่อ");

        var result = await h.Service().ReconcileAsync(Ct);

        Assert.Single(h.Db.EmailsTo(okEmail));
        Assert.Single(h.Db.Emails); // nobody else
        Assert.Equal(0, result.CoursesFailed);
        foreach (var id in new[] { missingId, brokenId })
        {
            var invite = h.Db.InviteFor(session.SessionId, id)!;
            Assert.Equal(InviteStatus.Skipped, invite.STATUS);
            Assert.Equal("no_contact", invite.ERROR);
        }

        Assert.Equal(InviteStatus.Invited, h.Db.InviteFor(session.SessionId, okId)!.STATUS);
        var emailCount = h.Db.Emails.Count;
        await h.Service().ReconcileAsync(Ct);
        Assert.Equal(emailCount, h.Db.Emails.Count);
    }

    [Fact]
    public async Task Reconcile_ContactsAreLookedUpOncePerCourse_NotOncePerLearner()
    {
        var h = NewHarness();
        var courseId = Guid.NewGuid();
        h.AddSession(courseId, TimeSpan.FromDays(1));
        for (var i = 0; i < 20; i++)
        {
            h.AddLearner(courseId);
        }

        await h.Service().ReconcileAsync(Ct);

        Assert.Equal(1, h.Contacts.BatchCalls);
    }

    // ---- 8. Failures: all-or-nothing per course -------------------------------------------------------------------

    [Fact]
    public async Task Reconcile_AFailureInOneCourse_RollsItBackWhole_AndTheOtherCoursesStillGetTheirEmails()
    {
        var h = NewHarness();
        var failingCourse = Guid.NewGuid();
        var healthyCourse = Guid.NewGuid();
        var failingSession = h.AddSession(failingCourse, TimeSpan.FromDays(1)); // sorts first
        var healthySession = h.AddSession(healthyCourse, TimeSpan.FromDays(2));
        var (poisonId, _) = h.AddLearner(failingCourse);
        var (healthyLearnerId, healthyEmail) = h.AddLearner(healthyCourse);
        h.Contacts.ThrowForUser = poisonId;

        var result = await h.Service().ReconcileAsync(Ct);

        Assert.Equal(1, result.CoursesFailed);
        Assert.Equal(1, result.CoursesProcessed);
        Assert.Single(h.Db.EmailsTo(healthyEmail));
        Assert.NotNull(h.Db.InviteFor(healthySession.SessionId, healthyLearnerId));

        // nothing half-applied for the failing course: no invite rows, no outbox rows, no notifications
        Assert.DoesNotContain(h.Db.Invites, i => i.SESSION_ID == failingSession.SessionId);
        Assert.DoesNotContain(h.Db.Notifications, n => n.UserId == poisonId);
        Assert.Single(h.Db.Emails);
        Assert.True(h.Db.ClearTrackingCount >= 1);
        Assert.Contains(failingCourse.ToString(), h.Logger.All);
        Assert.Contains(nameof(InvalidOperationException), h.Logger.All);

        // and it recovers by itself once the dependency is back
        h.Contacts.ThrowForUser = null;
        var retry = await h.Service().ReconcileAsync(Ct);
        Assert.Equal(0, retry.CoursesFailed);
        Assert.Equal(2, h.Db.Emails.Count);
        Assert.NotNull(h.Db.InviteFor(failingSession.SessionId, poisonId));
    }

    [Fact]
    public async Task Reconcile_AFailedSave_CommitsNothing_AndTheNextRunDoesTheWholeJob()
    {
        var h = NewHarness();
        var courseId = Guid.NewGuid();
        var session = h.AddSession(courseId, TimeSpan.FromDays(1));
        var (learnerId, learnerEmail) = h.AddLearner(courseId);
        h.Db.ThrowOnNextSave = new InvalidOperationException("db down");

        var failed = await h.Service().ReconcileAsync(Ct);

        Assert.Equal(1, failed.CoursesFailed);
        Assert.Empty(h.Db.Invites);
        Assert.Empty(h.Db.Emails);
        Assert.Empty(h.Db.Notifications);

        var recovered = await h.Service().ReconcileAsync(Ct);

        Assert.Equal(0, recovered.CoursesFailed);
        Assert.Single(h.Db.EmailsTo(learnerEmail));
        Assert.Equal(InviteStatus.Invited, h.Db.InviteFor(session.SessionId, learnerId)!.STATUS);
    }

    [Fact]
    public async Task Reconcile_TwoWorkersRacing_TheLoserHitsTheUniqueIndex_AndNothingIsDuplicated()
    {
        var h = NewHarness();
        var courseId = Guid.NewGuid();
        var session = h.AddSession(courseId, TimeSpan.FromDays(1));
        var (learnerId, learnerEmail) = h.AddLearner(courseId);

        // a rival worker commits the same (session, learner) row between this worker's read and its write
        h.Db.BeforeNextCommit = () => h.Db.Seed(SESSION_INVITE.Create(session.SessionId, learnerId, LiveParticipantRole.Learner, h.Clock));
        var loser = await h.Service().ReconcileAsync(Ct);

        // this unit is rolled back whole (the unique index refuses the duplicate); only the rival's row exists and nothing was sent
        Assert.Equal(1, loser.CoursesFailed);
        Assert.Single(h.Db.Invites, i => i.USER_ID == learnerId);
        Assert.Empty(h.Db.Emails);

        // the next run adopts the rival's row — still exactly one invite and one e-mail
        var next = await h.Service().ReconcileAsync(Ct);

        Assert.Equal(0, next.CoursesFailed);
        Assert.Single(h.Db.Invites, i => i.USER_ID == learnerId);
        Assert.Single(h.Db.EmailsTo(learnerEmail));
    }

    // ---- 9. Volume ----------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Reconcile_EmailBudget_StopsAtTheCap_AndTheNextRunFinishesTheRest()
    {
        var h = NewHarness();
        var courseId = Guid.NewGuid();
        var session = h.AddSession(courseId, TimeSpan.FromDays(1));
        var learners = Enumerable.Range(0, SessionInviteService.MaxEmailsPerRun + 50).Select(_ => h.AddLearner(courseId)).ToList();

        var first = await h.Service().ReconcileAsync(Ct);

        Assert.Equal(SessionInviteService.MaxEmailsPerRun, first.EmailsStaged);
        Assert.True(first.BudgetExhausted);
        Assert.Equal(SessionInviteService.MaxEmailsPerRun, h.Db.Emails.Count);

        var second = await h.Service().ReconcileAsync(Ct);

        Assert.Equal(50, second.EmailsStaged);
        Assert.False(second.BudgetExhausted);
        Assert.Equal(learners.Count, h.Db.Emails.Count);
        Assert.Equal(learners.Count, h.Db.Emails.Select(e => e.To).Distinct().Count()); // nobody got two
        Assert.All(learners, l => Assert.Equal(InviteStatus.Invited, h.Db.InviteFor(session.SessionId, l.UserId)!.STATUS));
    }

    [Fact]
    public async Task Reconcile_MoreCoursesThanOneRunCovers_RotatesSoEveryCourseIsEventuallyReached()
    {
        // start on an even rotation slot so the first run deterministically covers the first slice
        var start = LiveTestData.Now;
        if ((start.Ticks / (TimeSpan.TicksPerMinute * 2)) % 2 != 0)
        {
            start = start.AddMinutes(2);
        }

        var h = new InviteHarness(start);
        var learners = new List<(Guid UserId, string Email)>();
        for (var i = 0; i < SessionInviteService.MaxCoursesPerRun + 40; i++)
        {
            var courseId = Guid.NewGuid();
            h.AddSession(courseId, TimeSpan.FromDays(1) + TimeSpan.FromMinutes(i));
            learners.Add(h.AddLearner(courseId));
        }

        var first = await h.Service().ReconcileAsync(Ct);
        Assert.Equal(SessionInviteService.MaxCoursesPerRun, first.CoursesProcessed);

        h.Clock.UtcNow = h.Clock.UtcNow.AddMinutes(2); // the next scheduled run: the rotation moves to the next slice
        var second = await h.Service().ReconcileAsync(Ct);
        Assert.Equal(40, second.CoursesProcessed);

        Assert.Equal(learners.Count, h.Db.Emails.Select(e => e.To).Distinct().Count());
        Assert.Equal(learners.Count, h.Db.Emails.Count);
    }

    [Fact]
    public async Task Reconcile_MoreThanFiftySessions_AreSplitIntoSeveralEmails_EachWithItsOwnCalendar()
    {
        var h = NewHarness();
        var courseId = Guid.NewGuid();
        for (var i = 1; i <= 60; i++)
        {
            h.AddSession(courseId, TimeSpan.FromHours(30 * i));
        }

        var (learnerId, learnerEmail) = h.AddLearner(courseId);

        await h.Service().ReconcileAsync(Ct);

        var emails = h.Db.EmailsTo(learnerEmail).ToList();
        Assert.Equal(2, emails.Count);
        Assert.Equal(50, emails[0].EventCount);
        Assert.Equal(10, emails[1].EventCount);
        Assert.Equal("ยืนยันตารางเรียนสด: คอร์สทดสอบ", emails[0].Subject);
        Assert.Equal("เพิ่มคาบเรียนสดใหม่: คอร์สทดสอบ", emails[1].Subject);
        Assert.All(emails, e => Assert.True(e.CalendarIcs!.Length <= 200_000));
        Assert.Equal(60, h.Db.Invites.Count(i => i.USER_ID == learnerId && i.STATUS == InviteStatus.Invited));
    }

    [Fact]
    public async Task Reconcile_NothingScheduled_DoesNothingAtAll()
    {
        var h = NewHarness();

        var result = await h.Service().ReconcileAsync(Ct);

        Assert.Equal(new InviteReconcileResult(0, 0, 0, false), result);
        Assert.Equal(0, h.Learning.Calls);
    }

    // ---- 10. The room link never leaves the platform; no injection ---------------------------------------------------------

    private const string ZoomLink = "https://zoom.us/j/987654321?pwd=SECRETPASSCODE";
    private const string MeetLink = "https://meet.google.com/abc-defg-hij";

    [Fact]
    public async Task EveryMessageTheFlowProduces_ContainsOnlyThePlatformJoinUrl_NeverTheRoomLink()
    {
        var h = NewHarness();
        h.AddInstructorContact();
        var courseId = Guid.NewGuid();

        // real links are in the data, and an instructor even pasted them into free text
        var manual = h.AddSession(courseId, TimeSpan.FromDays(2), $"คาบ {MeetLink}", $"courseTitle {ZoomLink}", description: $"เข้าห้อง {ZoomLink} หรือ {MeetLink}");
        var google = h.AddSession(courseId, TimeSpan.FromDays(3), "คาบ google", $"courseTitle {ZoomLink}", description: MeetLink);
        h.AddManualMeeting(manual.SessionId, ZoomLink);
        h.AddGoogleMeeting(google.SessionId, MeetLink);
        var learner = h.AddLearner(courseId);
        var lapsing = h.AddLearner(courseId);

        await h.Service().ReconcileAsync(Ct); // invites (learner x2, instructor)
        h.UpdateSession(manual.SessionId, s => s with { StartsAtUtc = s.StartsAtUtc.AddHours(3), EndsAtUtc = s.EndsAtUtc.AddHours(3) });
        h.ChangeMeeting(manual.SessionId, m => m.MarkSessionChanged());
        h.Unenroll(courseId, lapsing.UserId);
        await h.Service().ReconcileAsync(Ct); // update + lapse
        h.UpdateSession(google.SessionId, s => s with { Status = LiveSessionStatus.Cancelled, CancelReason = $"ลิงก์ {MeetLink} เสีย" });
        h.ChangeMeeting(google.SessionId, m => m.MarkSessionCancelled());
        await h.Service().ReconcileAsync(Ct); // cancel
        h.Clock.UtcNow = manual.StartsAtUtc.AddHours(3).AddHours(-23); // 24 h reminder window
        await h.Service().SendRemindersAsync(Ct);

        Assert.True(h.Db.Emails.Count >= 6, $"only {h.Db.Emails.Count} e-mails were produced");
        foreach (var email in h.Db.Emails)
        {
            AssertNoRoomLink(email.Everything);
            Assert.All(
                Regex.Matches(email.Everything, @"https?://[^\s""'<>\\)]+").Select(m => m.Value),
                url => Assert.StartsWith(InviteHarness.PublicBaseUrl + "/", url, StringComparison.Ordinal));
        }

        foreach (var item in h.Db.Notifications)
        {
            AssertNoRoomLink($"{item.Title}\n{item.Body}\n{item.LinkUrl}");
        }

        AssertNoRoomLink(h.Logger.All);
        // the instructor sees the platform join URL in the calendar entry (the owner joins through the same link)
        Assert.Contains(h.Db.Emails, e => e.CalendarIcs is not null && e.CalendarIcs.Contains($"/live/{manual.SessionId:D}/join", StringComparison.Ordinal));
        _ = learner;
    }

    private static void AssertNoRoomLink(string text)
    {
        Assert.False(InviteHarness.ContainsMeetingHost(text), "a meeting host leaked");
        Assert.DoesNotContain("SECRETPASSCODE", text, StringComparison.Ordinal);
        Assert.DoesNotContain("abc-defg-hij", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task HostileText_InTitlesDescriptionsNamesAndReasons_CannotInjectHtmlOrCalendarProperties()
    {
        var h = NewHarness();
        h.AddInstructorContact();
        var courseId = Guid.NewGuid();
        const string script = "<script>alert(1)</script>";
        const string newline = "x\r\nATTENDEE;ROLE=CHAIR:mailto:attacker@example.test\r\nEND:VEVENT\r\nBEGIN:VEVENT";
        var session = h.AddSession(courseId, TimeSpan.FromDays(2), $"\"><img src=x onerror=1>{script}{newline}", $"{script}{newline}", description: $"{script}{newline}");
        h.AddManualMeeting(session.SessionId);
        var (learnerId, learnerEmail) = h.AddLearner(courseId, name: $"Bob\";ROLE=CHAIR:mailto:evil@example.test{newline}");

        await h.Service().ReconcileAsync(Ct);
        h.UpdateSession(session.SessionId, s => s with { Status = LiveSessionStatus.Cancelled, CancelReason = $"{script}{newline}" });
        h.ChangeMeeting(session.SessionId, m => m.MarkSessionCancelled());
        await h.Service().ReconcileAsync(Ct);

        Assert.True(h.Db.Emails.Count >= 3);
        var allowed = new[]
        {
            "BEGIN:", "END:", "VERSION:", "PRODID:", "CALSCALE:", "METHOD:", "X-WR-TIMEZONE:", "UID:", "DTSTAMP:", "SEQUENCE:", "DTSTART:",
            "DTEND:", "SUMMARY:", "DESCRIPTION:", "LOCATION:", "URL:", "ORGANIZER;", "ATTENDEE;", "TRANSP:", "STATUS:", "TRIGGER:", "ACTION:",
        };
        foreach (var email in h.Db.Emails)
        {
            Assert.DoesNotContain("<script>", email.BodyHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<img", email.BodyHtml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain('\r', email.Subject);
            Assert.DoesNotContain('\n', email.Subject);

            foreach (var line in email.IcsLines)
            {
                Assert.Contains(allowed, prefix => line.StartsWith(prefix, StringComparison.Ordinal));
            }

            // an attendee line is only ever the e-mail's own recipient — never an address smuggled in through a name or title
            foreach (var attendee in email.Property("ATTENDEE"))
            {
                Assert.EndsWith($":mailto:{email.To}", attendee, StringComparison.Ordinal);
            }

            Assert.Equal(email.EventCount, email.IcsLines.Count(l => l == "END:VEVENT"));
        }

        _ = learnerId;
    }
}
