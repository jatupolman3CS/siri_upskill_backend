using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Live.Domain;
using Siri.Modules.Live.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// P11-03 end to end over real HTTP against the production composition root (<see cref="SiriApiFactory"/>: real PostgreSQL + Redis from Testcontainers),
/// default Live configuration — Google switched OFF (no client id), so every room is a manually pasted link: the meeting row staged inside Catalog's
/// own transaction, the publish gate, ownership on every id, link validation, encryption at rest, "no room URL in any response", and the partitioned
/// rate limits. The OAuth flow itself is in <see cref="LiveGoogleOAuthIntegrationTests"/>.
/// <para>
/// Requires Docker like every test in this collection — on a machine without it these end in <c>DockerUnavailableException</c> (not an assertion).
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class LiveMeetingsIntegrationTests : IAsyncLifetime
{
    private const string ManualUrl = "https://zoom.us/j/987654321?pwd=SECRETPASSCODE";

    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;
    private HttpClient _client = null!;

    public LiveMeetingsIntegrationTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        _factory = new SiriApiFactory(_containers);

        // Migrations are applied by the test, never by the app (database.md: no Database.Migrate() in Program.cs).
        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // ---- The meeting row is staged by the real sink, atomically with the session ------------------------

    [Fact]
    public async Task CreateSession_StagesTheMeetingRow_InTheSameTransaction_AlreadyDecidedAsPasteALink()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);

        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var meeting = await db.SessionMeetings().AsNoTracking().SingleAsync(m => m.SESSION_ID == sessionId);

        // Changed from "Pending, provider undecided": the sink now makes the no-network part of the provider decision in the same SaveChanges as the class
        // (docs/DEPLOYMENT.md "Background jobs"), so this host - Google switched off, and no worker running at all - already says "paste the link". The
        // Pending-first behaviour survives only for rooms that need a Google call (see LiveProviderDecisionIntegrationTests).
        Assert.Equal(MeetingSyncStatus.AwaitingLink, meeting.SYNC_STATUS);
        Assert.Equal(MeetingProvider.Manual, meeting.PROVIDER);
        Assert.Equal(instructor.UserId, meeting.INSTRUCTOR_USER_ID);
        Assert.Null(meeting.MEET_URL_ENCRYPTED);
    }

    [Fact]
    public async Task CreateSession_RejectedByTheDomain_LeavesNoMeetingRowBehind()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);
        var firstSessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId, daysAhead: 3);

        // Overlaps the first session by an hour -> 409 from COURSE.AddLiveSession; nothing may be saved, including the staged meeting.
        using var overlapping = LiveIntegrationSupport.Authorized(HttpMethod.Post, $"/api/catalog/instructor/courses/{courseId}/live-sessions", instructor.Token);
        var start = DateTime.UtcNow.AddDays(3).AddHours(1);
        overlapping.Content = JsonContent.Create(new { title = "ทับกัน", description = (string?)null, startsAtUtc = start, endsAtUtc = start.AddHours(2) });
        using var response = await _client.SendAsync(overlapping);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await db.SessionMeetings().CountAsync(m => m.SESSION_ID == firstSessionId));
        Assert.Equal(1, await db.SessionMeetings().CountAsync(
            m => db.CourseLiveSessions().Any(s => s.Id == m.SESSION_ID && s.CourseId == courseId)));
    }

    // ---- Publish gate --------------------------------------------------------------------------------

    [Fact]
    public async Task SubmitForReview_LiveCourseWithAFutureSessionAndNoRoom_Returns400_WithTheReasonAndTheSessionIds()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId);

        var (status, body) = await LiveIntegrationSupport.SendAsync(
            _client, LiveIntegrationSupport.Authorized(HttpMethod.Post, $"/api/catalog/instructor/courses/{courseId}/submit-for-review", instructor.Token));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("live.meetings_not_ready", body.RootElement.GetProperty("reason").GetString());
        Assert.Equal("validation", body.RootElement.GetProperty("errorCode").GetString());
        Assert.Equal([sessionId], body.RootElement.GetProperty("sessionIds").EnumerateArray().Select(e => e.GetGuid()).ToArray());

        await using var scope = _factory.Services.CreateAsyncScope();
        var course = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Courses().AsNoTracking().SingleAsync(c => c.Id == courseId);
        Assert.Equal(CourseStatus.Draft, course.Status);
    }

    [Fact]
    public async Task SubmitForReview_AfterAValidLinkIsPasted_Succeeds()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId);

        var (linkStatus, _) = await LiveIntegrationSupport.SendAsync(_client, LiveIntegrationSupport.SetLinkRequest(instructor.Token, sessionId, ManualUrl));
        Assert.Equal(HttpStatusCode.OK, linkStatus);

        var (status, _) = await LiveIntegrationSupport.SendAsync(
            _client, LiveIntegrationSupport.Authorized(HttpMethod.Post, $"/api/catalog/instructor/courses/{courseId}/submit-for-review", instructor.Token));

        Assert.Equal(HttpStatusCode.OK, status);
        await using var scope = _factory.Services.CreateAsyncScope();
        var course = await scope.ServiceProvider.GetRequiredService<AppDbContext>().Courses().AsNoTracking().SingleAsync(c => c.Id == courseId);
        Assert.Equal(CourseStatus.InReview, course.Status);
    }

    [Fact]
    public async Task SubmitForReview_OnlyOneOfTwoSessionsHasARoom_ReportsJustTheOtherOne()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);
        var withRoom = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId, daysAhead: 2);
        var withoutRoom = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId, daysAhead: 4);
        await LiveIntegrationSupport.SendAsync(_client, LiveIntegrationSupport.SetLinkRequest(instructor.Token, withRoom, ManualUrl));

        var (status, body) = await LiveIntegrationSupport.SendAsync(
            _client, LiveIntegrationSupport.Authorized(HttpMethod.Post, $"/api/catalog/instructor/courses/{courseId}/submit-for-review", instructor.Token));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal([withoutRoom], body.RootElement.GetProperty("sessionIds").EnumerateArray().Select(e => e.GetGuid()).ToArray());
    }

    [Fact]
    public async Task Approve_RoomLostAfterSubmission_IsBlockedByTheSameGate()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (admin, adminToken) = await LiveIntegrationSupport.CreateUserAsync(_factory, _client, ROLE.AdminName);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId);
        await LiveIntegrationSupport.SendAsync(_client, LiveIntegrationSupport.SetLinkRequest(instructor.Token, sessionId, ManualUrl));
        var (submitStatus, _) = await LiveIntegrationSupport.SendAsync(
            _client, LiveIntegrationSupport.Authorized(HttpMethod.Post, $"/api/catalog/instructor/courses/{courseId}/submit-for-review", instructor.Token));
        Assert.Equal(HttpStatusCode.OK, submitStatus);
        Assert.NotEqual(Guid.Empty, admin.Id);

        // The room becomes unusable between submission and approval (the meeting row is marked deleted).
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var meeting = await db.SessionMeetings().SingleAsync(m => m.SESSION_ID == sessionId);
            meeting.MarkDeleted();
            await db.SaveChangesAsync();
        }

        var (status, body) = await LiveIntegrationSupport.SendAsync(
            _client, LiveIntegrationSupport.Authorized(HttpMethod.Post, $"/api/catalog/admin/courses/{courseId}/approve", adminToken));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal("live.meetings_not_ready", body.RootElement.GetProperty("reason").GetString());
    }

    // ---- PUT meeting-link ------------------------------------------------------------------------------

    [Fact]
    public async Task SetMeetingLink_Valid_Returns200Summary_WithoutTheUrl_AndStoresItEncrypted()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId);

        var (status, body) = await LiveIntegrationSupport.SendAsync(_client, LiveIntegrationSupport.SetLinkRequest(instructor.Token, sessionId, ManualUrl));

        Assert.Equal(HttpStatusCode.OK, status);
        var root = body.RootElement;
        Assert.Equal(sessionId, root.GetProperty("sessionId").GetGuid());
        Assert.Equal("Manual", root.GetProperty("provider").GetString());
        Assert.Equal("Synced", root.GetProperty("syncStatus").GetString());
        Assert.True(root.GetProperty("hasMeetingLink").GetBoolean());
        Assert.True(root.GetProperty("isUsable").GetBoolean());
        Assert.Equal("None", root.GetProperty("needsAction").GetString());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("errorCode").ValueKind);
        LiveIntegrationSupport.AssertNoRoomUrl(body, "987654321", "SECRETPASSCODE", "zoom.us");

        // What is physically in the table is ciphertext, not the link (raw SQL — independent of any entity mapping).
        var stored = await LiveIntegrationSupport.ReadRawColumnAsync(_factory, "MEET_URL_ENCRYPTED", sessionId);
        Assert.False(string.IsNullOrEmpty(stored));
        Assert.DoesNotContain("zoom.us", stored);
        Assert.DoesNotContain("SECRETPASSCODE", stored);
        await using var scope = _factory.Services.CreateAsyncScope();
        Assert.Equal(ManualUrl, scope.ServiceProvider.GetRequiredService<ISensitiveDataProtector>().Decrypt(stored!));
    }

    [Theory]
    [InlineData("javascript:alert(1)", "live.meeting_link_invalid")]
    [InlineData("http://meet.google.com/abc", "live.meeting_link_invalid")]
    [InlineData("https://meet.google.com@evil.example.test/", "live.meeting_link_invalid")]
    [InlineData("https://evil.example.test/room", "live.meeting_link_host_not_allowed")]
    [InlineData("https://meet.google.com.evil.example.test/x", "live.meeting_link_host_not_allowed")]
    [InlineData("", "live.meeting_link_invalid")]
    public async Task SetMeetingLink_Invalid_Returns400WithAStableReason_AndStoresNothing(string url, string reason)
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId);

        var (status, body) = await LiveIntegrationSupport.SendAsync(_client, LiveIntegrationSupport.SetLinkRequest(instructor.Token, sessionId, url));

        Assert.Equal(HttpStatusCode.BadRequest, status);
        Assert.Equal(reason, body.RootElement.GetProperty("reason").GetString());
        Assert.Null(await LiveIntegrationSupport.ReadRawColumnAsync(_factory, "MEET_URL_ENCRYPTED", sessionId));
    }

    [Fact]
    public async Task SetMeetingLink_AbsurdlyLongInput_IsRejectedBeforeAnyWork()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId);

        var (status, _) = await LiveIntegrationSupport.SendAsync(
            _client, LiveIntegrationSupport.SetLinkRequest(instructor.Token, sessionId, "https://zoom.us/" + new string('a', 5000)));

        Assert.Equal(HttpStatusCode.BadRequest, status);
    }

    [Fact]
    public async Task SetMeetingLink_CancelledSession_Returns409NotEditable()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId);
        var (cancelStatus, _) = await LiveIntegrationSupport.SendAsync(
            _client, LiveIntegrationSupport.Authorized(HttpMethod.Delete, $"/api/catalog/instructor/courses/{courseId}/live-sessions/{sessionId}", instructor.Token));
        Assert.Equal(HttpStatusCode.NoContent, cancelStatus);

        var (status, body) = await LiveIntegrationSupport.SendAsync(_client, LiveIntegrationSupport.SetLinkRequest(instructor.Token, sessionId, ManualUrl));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("live.session_not_editable", body.RootElement.GetProperty("reason").GetString());
    }

    // ---- Ownership / authorization ---------------------------------------------------------------------

    [Fact]
    public async Task SessionEndpoints_AnotherInstructor_Gets403_AndNothingChanges()
    {
        var owner = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var intruder = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, owner.ProfileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, owner.Token, courseId);

        var (putStatus, _) = await LiveIntegrationSupport.SendAsync(_client, LiveIntegrationSupport.SetLinkRequest(intruder.Token, sessionId, ManualUrl));
        var (resyncStatus, _) = await LiveIntegrationSupport.SendAsync(
            _client, LiveIntegrationSupport.Authorized(HttpMethod.Post, $"/api/live/instructor/sessions/{sessionId}/meeting/resync", intruder.Token));

        Assert.Equal(HttpStatusCode.Forbidden, putStatus);
        Assert.Equal(HttpStatusCode.Forbidden, resyncStatus);
        Assert.Null(await LiveIntegrationSupport.ReadRawColumnAsync(_factory, "MEET_URL_ENCRYPTED", sessionId));
    }

    [Fact]
    public async Task CourseMeetings_AnotherInstructor_GetsTheSame404AsAMissingCourse()
    {
        var owner = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var intruder = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, owner.ProfileId);
        await LiveIntegrationSupport.CreateSessionAsync(_client, owner.Token, courseId);

        var (foreignStatus, foreignBody) = await LiveIntegrationSupport.SendAsync(
            _client, LiveIntegrationSupport.Authorized(HttpMethod.Get, $"/api/live/instructor/courses/{courseId}/meetings", intruder.Token));
        var (missingStatus, missingBody) = await LiveIntegrationSupport.SendAsync(
            _client, LiveIntegrationSupport.Authorized(HttpMethod.Get, $"/api/live/instructor/courses/{Guid.NewGuid()}/meetings", intruder.Token));

        Assert.Equal(HttpStatusCode.NotFound, foreignStatus);
        Assert.Equal(HttpStatusCode.NotFound, missingStatus);
        // Indistinguishable apart from the per-request trace id.
        Assert.Equal(
            foreignBody.RootElement.GetProperty("title").GetString(),
            missingBody.RootElement.GetProperty("title").GetString());
        Assert.Equal(
            foreignBody.RootElement.GetProperty("errorCode").GetString(),
            missingBody.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task SessionEndpoints_UnknownSession_Returns404()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);

        var (putStatus, _) = await LiveIntegrationSupport.SendAsync(_client, LiveIntegrationSupport.SetLinkRequest(instructor.Token, Guid.NewGuid(), ManualUrl));
        var (resyncStatus, _) = await LiveIntegrationSupport.SendAsync(
            _client, LiveIntegrationSupport.Authorized(HttpMethod.Post, $"/api/live/instructor/sessions/{Guid.NewGuid()}/meeting/resync", instructor.Token));

        Assert.Equal(HttpStatusCode.NotFound, putStatus);
        Assert.Equal(HttpStatusCode.NotFound, resyncStatus);
    }

    [Fact]
    public async Task Admin_DoesNotBypassOwnership()
    {
        var owner = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (_, adminToken) = await LiveIntegrationSupport.CreateUserAsync(_factory, _client, ROLE.AdminName);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, owner.ProfileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, owner.Token, courseId);

        var (putStatus, _) = await LiveIntegrationSupport.SendAsync(_client, LiveIntegrationSupport.SetLinkRequest(adminToken, sessionId, ManualUrl));
        var (listStatus, _) = await LiveIntegrationSupport.SendAsync(
            _client, LiveIntegrationSupport.Authorized(HttpMethod.Get, $"/api/live/instructor/courses/{courseId}/meetings", adminToken));

        Assert.Equal(HttpStatusCode.Forbidden, putStatus);
        Assert.Equal(HttpStatusCode.NotFound, listStatus);
    }

    [Fact]
    public async Task EveryEndpoint_RequiresAnInstructor()
    {
        var (_, learnerToken) = await LiveIntegrationSupport.CreateUserAsync(_factory, _client, ROLE.LearnerName);
        var sessionId = Guid.NewGuid();
        var courseId = Guid.NewGuid();

        (HttpMethod Method, string Uri)[] endpoints =
        [
            (HttpMethod.Get, "/api/live/instructor/google/status"),
            (HttpMethod.Post, "/api/live/instructor/google/connect"),
            (HttpMethod.Delete, "/api/live/instructor/google"),
            (HttpMethod.Get, $"/api/live/instructor/courses/{courseId}/meetings"),
            (HttpMethod.Put, $"/api/live/instructor/sessions/{sessionId}/meeting-link"),
            (HttpMethod.Post, $"/api/live/instructor/sessions/{sessionId}/meeting/resync"),
        ];

        foreach (var (method, uri) in endpoints)
        {
            using var anonymous = new HttpRequestMessage(method, uri);
            using var anonymousResponse = await _client.SendAsync(anonymous);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);

            using var asLearner = LiveIntegrationSupport.Authorized(method, uri, learnerToken);
            using var learnerResponse = await _client.SendAsync(asLearner);
            Assert.Equal(HttpStatusCode.Forbidden, learnerResponse.StatusCode);
        }
    }

    // ---- Resync --------------------------------------------------------------------------------------

    [Fact]
    public async Task Resync_SyncedMeeting_Returns409NotResyncable()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId);
        await LiveIntegrationSupport.SendAsync(_client, LiveIntegrationSupport.SetLinkRequest(instructor.Token, sessionId, ManualUrl));

        var (status, body) = await LiveIntegrationSupport.SendAsync(
            _client, LiveIntegrationSupport.Authorized(HttpMethod.Post, $"/api/live/instructor/sessions/{sessionId}/meeting/resync", instructor.Token));

        Assert.Equal(HttpStatusCode.Conflict, status);
        Assert.Equal("live.meeting_not_resyncable", body.RootElement.GetProperty("reason").GetString());
    }

    [Fact]
    public async Task Resync_AwaitingLinkMeeting_GoesBackToPending()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            (await db.SessionMeetings().SingleAsync(m => m.SESSION_ID == sessionId)).ResolveAsAwaitingLink();
            await db.SaveChangesAsync();
        }

        var (status, body) = await LiveIntegrationSupport.SendAsync(
            _client, LiveIntegrationSupport.Authorized(HttpMethod.Post, $"/api/live/instructor/sessions/{sessionId}/meeting/resync", instructor.Token));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Pending", body.RootElement.GetProperty("syncStatus").GetString());
        Assert.Equal("Waiting", body.RootElement.GetProperty("needsAction").GetString());
    }

    // ---- Meetings list -------------------------------------------------------------------------------

    [Fact]
    public async Task CourseMeetings_ListsEachSessionsRoom_InScheduleOrder_WithTheNeedsActionTable()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, _) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);
        var later = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId, daysAhead: 5);
        var earlier = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId, daysAhead: 2);
        await LiveIntegrationSupport.SendAsync(_client, LiveIntegrationSupport.SetLinkRequest(instructor.Token, earlier, ManualUrl));

        var (status, body) = await LiveIntegrationSupport.SendAsync(
            _client, LiveIntegrationSupport.Authorized(HttpMethod.Get, $"/api/live/instructor/courses/{courseId}/meetings", instructor.Token));

        Assert.Equal(HttpStatusCode.OK, status);
        var items = body.RootElement.GetProperty("items").EnumerateArray().ToArray();
        Assert.Equal([earlier, later], items.Select(i => i.GetProperty("sessionId").GetGuid()).ToArray());
        Assert.Equal("None", items[0].GetProperty("needsAction").GetString());
        Assert.True(items[0].GetProperty("hasMeetingLink").GetBoolean());
        // Was "Waiting" (Pending until a worker ran). With Google off the sink decides at creation, so the instructor is told what to do straight away
        // instead of waiting for a job that, in a single-container deployment, would never have come.
        Assert.Equal("PasteLink", items[1].GetProperty("needsAction").GetString());
        Assert.False(items[1].GetProperty("hasMeetingLink").GetBoolean());
        LiveIntegrationSupport.AssertNoRoomUrl(body, "987654321", "SECRETPASSCODE", "zoom.us");
    }

    // ---- Google switched off ---------------------------------------------------------------------------

    [Fact]
    public async Task GoogleStatus_NoClientConfigured_ReportsConfiguredFalse_AndConnectIs503WithTheReason()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);

        var (statusCode, status) = await LiveIntegrationSupport.SendAsync(
            _client, LiveIntegrationSupport.Authorized(HttpMethod.Get, "/api/live/instructor/google/status", instructor.Token));
        Assert.Equal(HttpStatusCode.OK, statusCode);
        Assert.False(status.RootElement.GetProperty("configured").GetBoolean());
        Assert.False(status.RootElement.GetProperty("connected").GetBoolean());
        Assert.False(status.RootElement.GetProperty("needsReconnect").GetBoolean());
        Assert.Equal(0, status.RootElement.GetProperty("affectedSessionCount").GetInt32());

        using var connect = LiveIntegrationSupport.Authorized(HttpMethod.Post, "/api/live/instructor/google/connect", instructor.Token);
        connect.Content = JsonContent.Create(new { returnPath = "/instructor/live-settings" });
        var (connectStatus, connectBody) = await LiveIntegrationSupport.SendAsync(_client, connect);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, connectStatus);
        Assert.Equal("live.google_not_configured", connectBody.RootElement.GetProperty("reason").GetString());
        Assert.Equal("unavailable", connectBody.RootElement.GetProperty("errorCode").GetString());
    }

    [Fact]
    public async Task GoogleResponses_AreNeverCacheable()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);

        using var request = LiveIntegrationSupport.Authorized(HttpMethod.Get, "/api/live/instructor/google/status", instructor.Token);
        using var response = await _client.SendAsync(request);

        Assert.Contains("no-store", response.Headers.CacheControl?.ToString() ?? string.Empty);
    }

    // ---- No room URL anywhere public ----------------------------------------------------------------

    [Fact]
    public async Task PublicCourseDetailAndSearch_NeverExposeTheRoomUrl()
    {
        var instructor = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var (courseId, slug) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, instructor.ProfileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId);
        await LiveIntegrationSupport.SendAsync(_client, LiveIntegrationSupport.SetLinkRequest(instructor.Token, sessionId, ManualUrl));

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var course = await db.Courses().Include(c => c.LiveSessions).SingleAsync(c => c.Id == courseId);
            course.Publish(clock);
            await db.SaveChangesAsync();
        }

        var (detailStatus, detail) = await LiveIntegrationSupport.SendAsync(_client, new HttpRequestMessage(HttpMethod.Get, $"/api/catalog/courses/{slug}"));
        var (searchStatus, search) = await LiveIntegrationSupport.SendAsync(_client, new HttpRequestMessage(HttpMethod.Get, "/api/catalog/courses/search?format=Live"));

        Assert.Equal(HttpStatusCode.OK, detailStatus);
        Assert.Equal(HttpStatusCode.OK, searchStatus);
        LiveIntegrationSupport.AssertNoRoomUrl(detail, "987654321", "SECRETPASSCODE", "zoom.us");
        LiveIntegrationSupport.AssertNoRoomUrl(search, "987654321", "SECRETPASSCODE", "zoom.us");
    }

    // ---- Rate limits ----------------------------------------------------------------------------------

    [Fact]
    public async Task LiveUserRateLimit_61stRequestInAMinuteIs429_ButAnotherInstructorIsUnaffected()
    {
        var alice = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);
        var bob = await LiveIntegrationSupport.CreateInstructorAsync(_factory, _client);

        for (var i = 0; i < 60; i++)
        {
            using var request = LiveIntegrationSupport.Authorized(HttpMethod.Get, "/api/live/instructor/google/status", alice.Token);
            using var response = await _client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        }

        using var over = LiveIntegrationSupport.Authorized(HttpMethod.Get, "/api/live/instructor/google/status", alice.Token);
        using var overResponse = await _client.SendAsync(over);
        Assert.Equal(HttpStatusCode.TooManyRequests, overResponse.StatusCode);

        using var other = LiveIntegrationSupport.Authorized(HttpMethod.Get, "/api/live/instructor/google/status", bob.Token);
        using var otherResponse = await _client.SendAsync(other);
        Assert.Equal(HttpStatusCode.OK, otherResponse.StatusCode);
    }

    [Fact]
    public async Task GoogleCallbackRateLimit_61stAnonymousRequestInAMinuteIs429()
    {
        using var noRedirect = _factory.CreateClient(new WebApplicationFactoryClientOptions { AllowAutoRedirect = false });

        for (var i = 0; i < 60; i++)
        {
            using var response = await noRedirect.GetAsync("/api/live/instructor/google/callback?state=bogus&code=x");
            Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        }

        using var over = await noRedirect.GetAsync("/api/live/instructor/google/callback?state=bogus&code=x");
        Assert.Equal(HttpStatusCode.TooManyRequests, over.StatusCode);
    }
}
