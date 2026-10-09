using System.Net;
using System.Net.Http.Json;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.Integrations.Google;
using Siri.IntegrationTests.Fixtures;
using Siri.IntegrationTests.TestData;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning.Domain;
using Siri.Modules.Learning.Infrastructure;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.Modules.Live.Infrastructure;
using Siri.Modules.Notification.Domain;
using Siri.Modules.Notification.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// P11-04 end to end against the production composition root (<see cref="SiriApiFactory"/>: real PostgreSQL + Redis from Testcontainers): the
/// purchase-day invite, latecomers, revoke, cancel, reschedule, instructor invites, reminders, idempotency and — the security-critical one —
/// that no e-mail, calendar file or notification ever carries the real room URL. Sessions are created through the real endpoint (so Catalog's handler
/// stages the meeting row through the real sink), rooms are pasted through the real endpoint, and the jobs' services are resolved from the real container.
/// <para>
/// The e-mails are asserted from the <c>NOTIFY.EMAIL_OUTBOX</c> table (nothing goes through SMTP). Requires Docker like every test in this collection —
/// on a machine without it these end in <c>DockerUnavailableException</c>, not an assertion. All tests share one database, so every assertion is scoped to
/// the recipients/sessions the test itself created.
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class LiveInviteIntegrationTests : IAsyncLifetime
{
    private const string RoomUrl = "https://zoom.us/j/987654321?pwd=SECRETPASSCODE";
    private static readonly string[] MeetingHosts = ["meet.google.com", "zoom.us", "teams.microsoft.com", "teams.live.com"];

    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;
    private HttpClient _client = null!;

    public LiveInviteIntegrationTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        _factory = new SiriApiFactory(_containers);

        // Migrations are applied by the test, never by the app (database.md: no Database.Migrate() in Program.cs).
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        // The reconcile job sweeps every course but handles at most SessionInviteService.MaxCoursesPerRun (100) per run, rotating which ones by the clock. With
        // the live courses every earlier class leaves in this shared database, a single reconcile call could miss the course a test just created - so start
        // from a clean slate of live courses: each test here builds its own.
        await LiveIntegrationSupport.RetireAllLiveCoursesAsync(_factory);

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // ---- Arrange helpers ---------------------------------------------------------------------------------

    private sealed record Arranged(TestInstructor Instructor, Guid CourseId, string CourseSlug, IReadOnlyList<Guid> SessionIds);

    private sealed record Learner(Guid UserId, string Email, Guid EnrollmentId);

    /// <summary>An approved instructor, a Live course and <paramref name="sessionCount"/> sessions (2, 3, ... days ahead) created through the real endpoint.
    /// With <paramref name="withActiveGoogleAccount"/> the instructor has a connected Google account, so the sink leaves each room undecided (Pending, no
    /// provider yet) for the job instead of deciding "paste a link" at creation - the state a room needing a Google call is really in.</summary>
    private async Task<Arranged> ArrangeAsync(int sessionCount, bool pasteRooms = false, bool withActiveGoogleAccount = false)
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        if (withActiveGoogleAccount)
        {
            await using var scope = _factory.Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.InstructorGoogleAccounts().Add(INSTRUCTOR_GOOGLE_ACCOUNT.Connect(
                instructor.UserId, $"sub-{Guid.NewGuid():N}", "teacher@gmail.test", "enc-refresh", GoogleScopes.CalendarEventsOwned, scope.ServiceProvider.GetRequiredService<IClock>()));
            await db.SaveChangesAsync();
        }

        var (courseId, slug) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);

        var sessionIds = new List<Guid>();
        for (var i = 0; i < sessionCount; i++)
        {
            sessionIds.Add(await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId, daysAhead: 2 + i));
        }

        if (pasteRooms)
        {
            foreach (var sessionId in sessionIds)
            {
                await PasteRoomAsync(instructor, sessionId);
            }
        }

        return new Arranged(instructor, courseId, slug, sessionIds);
    }

    private async Task PasteRoomAsync(TestInstructor instructor, Guid sessionId)
    {
        var (status, _) = await LiveIntegrationSupport.SendAsync(_client, LiveIntegrationSupport.SetLinkRequest(instructor.Token, sessionId, RoomUrl));
        Assert.Equal(HttpStatusCode.OK, status);
    }

    private async Task<Learner> EnrollLearnerAsync(Guid courseId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var user = await new TestUserBuilder().BuildAsync(scope.ServiceProvider);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var enrollment = ENROLLMENT.Create(user.Id, courseId, null, EnrollmentSource.Purchase, null, clock);
        db.Enrollments().Add(enrollment);
        await db.SaveChangesAsync();

        return new Learner(user.Id, user.Email, enrollment.ENROLLMENT_ID);
    }

    private async Task RunReconcileAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SessionInviteService>().ReconcileAsync(CancellationToken.None);
    }

    private async Task RunRemindersAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SessionInviteService>().SendRemindersAsync(CancellationToken.None);
    }

    private async Task<List<EMAIL_OUTBOX_MESSAGE>> EmailsToAsync(string email)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().EmailOutboxMessages()
            .AsNoTracking()
            .Where(m => m.ToEmail == email)
            .OrderBy(m => m.Id)
            .ToListAsync();
    }

    private async Task<List<USER_NOTIFICATION>> NotificationsOfAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().UserNotifications()
            .AsNoTracking()
            .Where(n => n.UserId == userId)
            .OrderBy(n => n.CreatedAtUtc)
            .ToListAsync();
    }

    private async Task<List<SESSION_INVITE>> InvitesAsync(IEnumerable<Guid> sessionIds)
    {
        var ids = sessionIds.ToArray();
        await using var scope = _factory.Services.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>().SessionInvites()
            .AsNoTracking()
            .Where(i => ids.Contains(i.SESSION_ID))
            .ToListAsync();
    }

    private async Task<string> EmailOfAsync(Guid userId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var email = await scope.ServiceProvider.GetRequiredService<IUserContactReader>().GetEmailAsync(userId, CancellationToken.None);
        return Assert.IsType<string>(email);
    }

    private static IReadOnlyList<string> IcsLines(string ics) =>
        ics.Replace("\r\n ", string.Empty, StringComparison.Ordinal).Split("\r\n", StringSplitOptions.RemoveEmptyEntries);

    private static int Events(EMAIL_OUTBOX_MESSAGE message) => IcsLines(message.CalendarIcs!).Count(l => l == "BEGIN:VEVENT");

    private static string Everything(EMAIL_OUTBOX_MESSAGE message) =>
        $"{message.Subject}\n{message.BodyHtml}\n{(message.CalendarIcs is null ? string.Empty : string.Join("\n", IcsLines(message.CalendarIcs)))}";

    /// <summary>Pretends the invitations went out earlier. The reminder flags are cleared too: an invitation sent inside the 24 h window
    /// legitimately marks the elapsed reminders as sent (<c>SessionInvitePolicy.SuppressElapsedReminders</c>), and these tests are about the
    /// reminders an earlier invitation would still owe.</summary>
    private async Task SetInviteSentAtAsync(Guid sessionId, DateTime sentAtUtc)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE \"LIVE\".\"SESSION_INVITES\" SET \"INVITE_SENT_AT_UTC\" = {sentAtUtc}, \"REMINDER_24H_SENT_AT_UTC\" = NULL, \"REMINDER_1H_SENT_AT_UTC\" = NULL WHERE \"SESSION_ID\" = {sessionId}");
    }

    // ---- 1. Purchase day -------------------------------------------------------------------------------------

    [Fact]
    public async Task PurchaseDay_TheLearnerGetsOneBatchEmail_WithAPublishCalendarHoldingEveryFutureSession_AndOneNotification()
    {
        var arranged = await ArrangeAsync(sessionCount: 3);
        var learner = await EnrollLearnerAsync(arranged.CourseId);

        await RunReconcileAsync();

        var email = Assert.Single(await EmailsToAsync(learner.Email));
        Assert.Equal("live-invite-batch", email.TemplateKey);
        Assert.Equal("PUBLISH", email.CalendarMethod);
        Assert.Contains("METHOD:PUBLISH", IcsLines(email.CalendarIcs!));
        Assert.Equal(3, Events(email));
        foreach (var sessionId in arranged.SessionIds)
        {
            Assert.Contains(IcsLines(email.CalendarIcs!), l => l.StartsWith($"UID:{sessionId:N}@", StringComparison.Ordinal));
            Assert.Contains($"/live/{sessionId:D}/join", email.BodyHtml, StringComparison.Ordinal);
        }

        var notification = Assert.Single(await NotificationsOfAsync(learner.UserId), n => n.Type == "live.invite");
        Assert.Equal($"/learn/{arranged.CourseSlug}?tab=live", notification.LinkUrl);

        var invites = (await InvitesAsync(arranged.SessionIds)).Where(i => i.USER_ID == learner.UserId).ToList();
        Assert.Equal(3, invites.Count);
        Assert.All(invites, i => Assert.Equal(InviteStatus.Invited, i.STATUS));
    }

    [Fact]
    public async Task Reconcile_RunThreeTimesInARow_NeverSendsMore()
    {
        var arranged = await ArrangeAsync(sessionCount: 2, pasteRooms: true);
        var learner = await EnrollLearnerAsync(arranged.CourseId);
        var instructorEmail = await EmailOfAsync(arranged.Instructor.UserId);

        await RunReconcileAsync();
        var learnerEmails = (await EmailsToAsync(learner.Email)).Count;
        var instructorEmails = (await EmailsToAsync(instructorEmail)).Count;
        var invites = (await InvitesAsync(arranged.SessionIds)).Count;
        Assert.Equal(1, learnerEmails);
        Assert.Equal(1, instructorEmails);

        await RunReconcileAsync();
        await RunReconcileAsync();

        Assert.Equal(learnerEmails, (await EmailsToAsync(learner.Email)).Count);
        Assert.Equal(instructorEmails, (await EmailsToAsync(instructorEmail)).Count);
        Assert.Equal(invites, (await InvitesAsync(arranged.SessionIds)).Count);
        Assert.Single(await NotificationsOfAsync(learner.UserId));
    }

    [Fact]
    public async Task TwoWorkersReconcilingAtOnce_NeverDuplicateAnInviteOrAnEmail()
    {
        var arranged = await ArrangeAsync(sessionCount: 2);
        var learner = await EnrollLearnerAsync(arranged.CourseId);

        // the unique (SESSION_ID, USER_ID) index makes the loser's whole course roll back — outbox rows included
        await Task.WhenAll(RunReconcileAsync(), RunReconcileAsync());
        await RunReconcileAsync();

        Assert.Single(await EmailsToAsync(learner.Email));
        var invites = (await InvitesAsync(arranged.SessionIds)).Where(i => i.USER_ID == learner.UserId).ToList();
        Assert.Equal(2, invites.Count);
        Assert.Equal(2, invites.Select(i => i.SESSION_ID).Distinct().Count());
    }

    // ---- 2. Latecomer ----------------------------------------------------------------------------------------------

    [Fact]
    public async Task Latecomer_ASessionThatIsAlreadyRunning_IsNeverInvitedTo()
    {
        var arranged = await ArrangeAsync(sessionCount: 2);
        var running = arranged.SessionIds[0];
        var upcoming = arranged.SessionIds[1];

        // the instructor's schedule edit makes the first session start 30 minutes ago (allowed while it is still running)
        using var request = LiveIntegrationSupport.Authorized(
            HttpMethod.Put, $"/api/catalog/instructor/courses/{arranged.CourseId}/live-sessions/{running}", arranged.Instructor.Token);
        request.Content = JsonContent.Create(new
        {
            title = "คาบที่กำลังสอน",
            description = (string?)null,
            startsAtUtc = DateTime.UtcNow.AddMinutes(-30),
            endsAtUtc = DateTime.UtcNow.AddHours(1),
        });
        using (var response = await _client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        var learner = await EnrollLearnerAsync(arranged.CourseId);
        await RunReconcileAsync();

        var email = Assert.Single(await EmailsToAsync(learner.Email));
        Assert.Equal(1, Events(email));
        Assert.Contains(IcsLines(email.CalendarIcs!), l => l.StartsWith($"UID:{upcoming:N}@", StringComparison.Ordinal));
        Assert.DoesNotContain(await InvitesAsync([running]), i => i.USER_ID == learner.UserId);
    }

    // ---- 3. Revoke / expire --------------------------------------------------------------------------------------------

    [Fact]
    public async Task RevokedEnrollment_SendsACancelCalendar_WithTheNextSequence_AndOnlyOnce()
    {
        var arranged = await ArrangeAsync(sessionCount: 2);
        var learner = await EnrollLearnerAsync(arranged.CourseId);
        await RunReconcileAsync();

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var enrollment = await db.Enrollments().SingleAsync(e => e.ENROLLMENT_ID == learner.EnrollmentId);
            enrollment.Revoke();
            await db.SaveChangesAsync();
        }

        await RunReconcileAsync();

        var cancel = Assert.Single(await EmailsToAsync(learner.Email), e => e.TemplateKey == "live-invite-lapsed");
        Assert.Equal("CANCEL", cancel.CalendarMethod);
        Assert.Contains("METHOD:CANCEL", IcsLines(cancel.CalendarIcs!));
        Assert.Equal(2, Events(cancel));
        Assert.All(IcsLines(cancel.CalendarIcs!).Where(l => l.StartsWith("SEQUENCE:", StringComparison.Ordinal)), l => Assert.Equal("SEQUENCE:1", l));

        var invites = (await InvitesAsync(arranged.SessionIds)).Where(i => i.USER_ID == learner.UserId).ToList();
        Assert.All(invites, i =>
        {
            Assert.Equal(InviteStatus.Cancelled, i.STATUS);
            Assert.Equal(1, i.ICS_SEQUENCE_SENT);
        });

        var total = (await EmailsToAsync(learner.Email)).Count;
        await RunReconcileAsync();
        Assert.Equal(total, (await EmailsToAsync(learner.Email)).Count);
    }

    // ---- 4. Cancelled session -------------------------------------------------------------------------------------------

    [Fact]
    public async Task CancelSession_EveryInvitedLearnerGetsExactlyOneCancel()
    {
        var arranged = await ArrangeAsync(sessionCount: 2);
        var learners = new List<Learner> { await EnrollLearnerAsync(arranged.CourseId), await EnrollLearnerAsync(arranged.CourseId) };
        await RunReconcileAsync();
        var cancelled = arranged.SessionIds[1];

        using var request = LiveIntegrationSupport.Authorized(
            HttpMethod.Post, $"/api/catalog/instructor/courses/{arranged.CourseId}/live-sessions/{cancelled}/cancel", arranged.Instructor.Token);
        request.Content = JsonContent.Create(new { reason = "ผู้สอนป่วย" });
        using (var response = await _client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await RunReconcileAsync();
        await RunReconcileAsync();

        foreach (var learner in learners)
        {
            var cancel = Assert.Single(await EmailsToAsync(learner.Email), e => e.TemplateKey == "live-session-cancelled");
            Assert.Equal("CANCEL", cancel.CalendarMethod);
            Assert.Contains("STATUS:CANCELLED", IcsLines(cancel.CalendarIcs!));
            Assert.Contains($"UID:{cancelled:N}@", string.Join("\n", IcsLines(cancel.CalendarIcs!)), StringComparison.Ordinal);
            Assert.Contains("ผู้สอนป่วย", cancel.BodyHtml, StringComparison.Ordinal);
        }
    }

    // ---- 5. Reschedule --------------------------------------------------------------------------------------------------

    [Fact]
    public async Task RescheduleSession_SendsARequestWithTheBumpedSequence_ThroughTheRealMeetingSequence()
    {
        var arranged = await ArrangeAsync(sessionCount: 1);
        var sessionId = arranged.SessionIds[0];
        var learner = await EnrollLearnerAsync(arranged.CourseId);
        await RunReconcileAsync();

        using var request = LiveIntegrationSupport.Authorized(
            HttpMethod.Put, $"/api/catalog/instructor/courses/{arranged.CourseId}/live-sessions/{sessionId}", arranged.Instructor.Token);
        request.Content = JsonContent.Create(new
        {
            title = "คาบที่เลื่อนเวลา",
            description = (string?)null,
            startsAtUtc = DateTime.UtcNow.AddDays(5),
            endsAtUtc = DateTime.UtcNow.AddDays(5).AddHours(2),
        });
        using (var response = await _client.SendAsync(request))
        {
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await RunReconcileAsync();

        var update = Assert.Single(await EmailsToAsync(learner.Email), e => e.TemplateKey == "live-session-updated");
        Assert.Equal("REQUEST", update.CalendarMethod);
        Assert.Contains("SEQUENCE:1", IcsLines(update.CalendarIcs!));
        var invite = Assert.Single(await InvitesAsync([sessionId]), i => i.USER_ID == learner.UserId);
        Assert.Equal(1, invite.ICS_SEQUENCE_SENT);

        var total = (await EmailsToAsync(learner.Email)).Count;
        await RunReconcileAsync();
        Assert.Equal(total, (await EmailsToAsync(learner.Email)).Count);
    }

    // ---- 6. Instructor ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Instructor_IsInvitedOnlyOnceTheRoomIsDecided_WithAPublishCalendar()
    {
        // With a connected Google account the room needs a Google call, so the sink leaves it undecided for the job (without one it would already be
        // "paste a link" - a decided provider - and the instructor invited at once).
        var arranged = await ArrangeAsync(sessionCount: 2, withActiveGoogleAccount: true);
        var instructorEmail = await EmailOfAsync(arranged.Instructor.UserId);

        await RunReconcileAsync();
        Assert.Empty(await EmailsToAsync(instructorEmail)); // the meeting row exists (staged by the sink) but its provider is not decided yet

        await PasteRoomAsync(arranged.Instructor, arranged.SessionIds[0]);
        await PasteRoomAsync(arranged.Instructor, arranged.SessionIds[1]);
        await RunReconcileAsync();

        var email = Assert.Single(await EmailsToAsync(instructorEmail));
        Assert.Equal("live-instructor-batch", email.TemplateKey);
        Assert.Equal("PUBLISH", email.CalendarMethod);
        Assert.Equal(2, Events(email));
        Assert.Contains($"/instructor/sessions/{arranged.SessionIds[0]:D}", email.BodyHtml, StringComparison.Ordinal);
        Assert.Contains(await NotificationsOfAsync(arranged.Instructor.UserId), n => n.Type == "live.invite");
    }

    [Fact]
    public async Task Instructor_WithoutGoogle_IsInvitedAtOnce_BecauseTheProviderIsDecidedWhenTheClassIsCreated()
    {
        var arranged = await ArrangeAsync(sessionCount: 2);
        var instructorEmail = await EmailOfAsync(arranged.Instructor.UserId);

        await RunReconcileAsync(); // no worker has run, no room was pasted - the sink already decided "paste a link" (a decided provider)

        var email = Assert.Single(await EmailsToAsync(instructorEmail));
        Assert.Equal("live-instructor-batch", email.TemplateKey);
        Assert.Equal(2, Events(email));
    }

    // ---- 7. Reminders ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task TwentyFourHourReminder_GoesToTheLearnerAndTheInstructor_OnceOnly()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId, daysAhead: 0, hourOffset: 20);
        await PasteRoomAsync(instructor, sessionId);
        var learner = await EnrollLearnerAsync(courseId);
        var instructorEmail = await EmailOfAsync(instructor.UserId);
        await RunReconcileAsync();

        // the invitation e-mails went out "just now"; pretend they went out three days ago so the 24 h reminder is owed
        await SetInviteSentAtAsync(sessionId, DateTime.UtcNow.AddDays(-3));

        await RunRemindersAsync();
        await RunRemindersAsync();

        var learnerReminder = Assert.Single(await EmailsToAsync(learner.Email), e => e.TemplateKey == "live-reminder-24h");
        Assert.Equal("REQUEST", learnerReminder.CalendarMethod);
        Assert.Single(await EmailsToAsync(instructorEmail), e => e.TemplateKey == "live-reminder-24h");
        Assert.Contains(await NotificationsOfAsync(learner.UserId), n => n.Type == "live.reminder_24h");

        var invites = await InvitesAsync([sessionId]);
        Assert.All(invites, i => Assert.NotNull(i.REMINDER_24H_SENT_AT_UTC));
    }

    // ---- 8. The room link never leaves the platform (security-critical) ------------------------------------------------------

    [Fact]
    public async Task NoEmailCalendarOrNotification_EverCarriesTheRoomUrl()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);
        var soon = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId, daysAhead: 0, hourOffset: 20);
        var later = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId, daysAhead: 3);
        await PasteRoomAsync(instructor, soon);
        await PasteRoomAsync(instructor, later);
        var learner = await EnrollLearnerAsync(courseId);
        var instructorEmail = await EmailOfAsync(instructor.UserId);

        await RunReconcileAsync();
        await SetInviteSentAtAsync(soon, DateTime.UtcNow.AddDays(-3));
        await RunRemindersAsync();

        using (var cancel = LiveIntegrationSupport.Authorized(HttpMethod.Post, $"/api/catalog/instructor/courses/{courseId}/live-sessions/{later}/cancel", instructor.Token))
        {
            cancel.Content = JsonContent.Create(new { reason = "ยกเลิก" });
            using var response = await _client.SendAsync(cancel);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        await RunReconcileAsync();

        var emails = (await EmailsToAsync(learner.Email)).Concat(await EmailsToAsync(instructorEmail)).ToList();
        Assert.True(emails.Count >= 4, $"only {emails.Count} e-mails were produced");
        foreach (var email in emails)
        {
            AssertNoRoomUrl(Everything(email));
            Assert.All(
                Regex.Matches(Everything(email), @"https?://[^\s""'<>\\)]+").Select(m => m.Value),
                url => Assert.False(MeetingHosts.Any(host => url.Contains(host, StringComparison.OrdinalIgnoreCase)), $"{email.TemplateKey} links to a meeting host"));
        }

        foreach (var notification in (await NotificationsOfAsync(learner.UserId)).Concat(await NotificationsOfAsync(instructor.UserId)))
        {
            AssertNoRoomUrl($"{notification.Title}\n{notification.Body}\n{notification.LinkUrl}");
        }

        // and the platform join URL IS what the calendar entry points at
        Assert.Contains(emails, e => e.CalendarIcs is not null && Everything(e).Contains($"/live/{soon:D}/join", StringComparison.Ordinal));
    }

    private static void AssertNoRoomUrl(string text)
    {
        Assert.DoesNotContain("SECRETPASSCODE", text, StringComparison.Ordinal);
        foreach (var host in MeetingHosts)
        {
            Assert.DoesNotContain(host, text, StringComparison.OrdinalIgnoreCase);
        }
    }

    // ---- 9. Hostile text ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task HostileSessionTitle_IsEncodedInTheBody_AndCannotInjectCalendarProperties()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);
        const string hostile = "<script>alert(1)</script>\r\nATTENDEE;ROLE=CHAIR:mailto:attacker@example.test";

        using var create = LiveIntegrationSupport.Authorized(HttpMethod.Post, $"/api/catalog/instructor/courses/{courseId}/live-sessions", instructor.Token);
        var start = DateTime.UtcNow.AddDays(2);
        create.Content = JsonContent.Create(new { title = hostile, description = hostile, startsAtUtc = start, endsAtUtc = start.AddHours(2) });
        using var response = await _client.SendAsync(create);
        if (response.StatusCode != HttpStatusCode.Created)
        {
            // the endpoint's own validation may already refuse a title with control characters — that is an equally good outcome
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            return;
        }

        var learner = await EnrollLearnerAsync(courseId);
        await RunReconcileAsync();

        var email = Assert.Single(await EmailsToAsync(learner.Email));
        Assert.DoesNotContain("<script>", email.BodyHtml, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain('\r', email.Subject);
        Assert.DoesNotContain('\n', email.Subject);
        Assert.DoesNotContain(IcsLines(email.CalendarIcs!), l => l.StartsWith("ATTENDEE", StringComparison.Ordinal));
    }
}
