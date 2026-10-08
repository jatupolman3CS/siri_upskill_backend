using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.IntegrationTests.Fixtures;
using Siri.IntegrationTests.TestData;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Learning.Domain;
using Siri.Modules.Learning.Infrastructure;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Contracts;
using Siri.Modules.Live.Domain;
using Siri.Modules.Live.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// P11-05 end to end over real HTTP against the production composition root (<see cref="SiriApiFactory"/>: real PostgreSQL + Redis from Testcontainers): the
/// <b>join gate</b> — who may obtain the raw room link, when, and that the platform recorded it first — plus my-sessions, upcoming, calendar.ics, the instructor
/// session list/detail/roster and the Catalog live-schedule read. The behaviours that matter most:
/// <list type="bullet">
/// <item>the link leaves the system only through <c>POST …/join</c> and the owning instructor's session detail — a recursive JSON walk of every other response proves it;</item>
/// <item>unknown / not entitled / lapsed enrollment all answer the <b>byte-identical</b> 404 (an IDOR matrix over join, calendar and my-sessions);</item>
/// <item>the join log row exists when the link is returned, and if committing it fails no link is returned;</item>
/// <item>the partitioned <c>live-join</c> rate limit: the 7th request in a minute is 429, another user is unaffected.</item>
/// </list>
/// Access tokens are minted directly with the real <see cref="IAccessTokenGenerator"/> (the host's own signing key), not through <c>/login</c>, because the "auth" limiter allows only
/// five logins a minute and these tests need many users. Times are relative to the real clock (the host's <c>IClock</c> is the system clock).
/// <para>
/// Requires Docker like every test in this collection — on a machine without it these end in <c>DockerUnavailableException</c> (not an assertion).
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class LiveJoinGateIntegrationTests : IAsyncLifetime
{
    private const string RoomUrl = "https://zoom.us/j/987654321?pwd=SECRETPASSCODE";
    private const string RoomSecret = "SECRETPASSCODE";
    private const string UserAgent = "LiveGateTest/1.0";

    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;
    private HttpClient _client = null!;

    public LiveJoinGateIntegrationTests(ContainersFixture containers)
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

    // ---- Arrange helpers ------------------------------------------------------------------------------

    private sealed record Actor(Guid UserId, string Token, Guid AuthSessionId, string Email);

    private sealed record Scene(Actor Instructor, Guid ProfileId, Guid CourseId, string CourseSlug, Guid SessionId);

    private sealed record Reply(
        HttpStatusCode Status,
        string Body,
        bool NoStore,
        bool PragmaNoCache,
        TimeSpan? RetryAfter,
        string? ContentType,
        string? ContentDisposition)
    {
        public JsonDocument Json => JsonDocument.Parse(string.IsNullOrWhiteSpace(Body) ? "{}" : Body);

        /// <summary>The ProblemDetails body without the one member that is allowed to differ between two otherwise identical errors.</summary>
        public string WithoutTraceId()
        {
            var node = JsonNode.Parse(Body)!.AsObject();
            node.Remove("traceId");
            return node.ToJsonString();
        }
    }

    private async Task<Actor> CreateActorAsync(string roleName)
    {
        var builder = new TestUserBuilder().WithRole(roleName);

        await using var scope = _factory.Services.CreateAsyncScope();
        var user = await builder.BuildAsync(scope.ServiceProvider);

        var sid = Guid.NewGuid();
        var (token, _) = scope.ServiceProvider.GetRequiredService<IAccessTokenGenerator>().Generate(user, sid);

        return new Actor(user.Id, token, sid, builder.Email);
    }

    private async Task<(Actor Actor, Guid ProfileId)> CreateInstructorAsync()
    {
        var builder = new TestUserBuilder().WithRole(ROLE.InstructorName);

        await using var scope = _factory.Services.CreateAsyncScope();
        var user = await builder.BuildAsync(scope.ServiceProvider);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var profile = INSTRUCTOR_PROFILE.Apply(user.Id, "Test Instructor", "Headline", "Bio");
        profile.Approve(clock);
        db.InstructorProfiles().Add(profile);
        await db.SaveChangesAsync();

        var sid = Guid.NewGuid();
        var (token, _) = scope.ServiceProvider.GetRequiredService<IAccessTokenGenerator>().Generate(user, sid);

        return (new Actor(user.Id, token, sid, builder.Email), profile.Id);
    }

    /// <summary>
    /// An instructor, a live course and one session created through the real endpoint (so the meeting row is staged by the real sink), with a pasted link when
    /// <paramref name="withLink"/>; the session is then moved to start <paramref name="startsIn"/> from now (negative = in the past) and last two hours.
    /// </summary>
    private async Task<Scene> CreateSceneAsync(TimeSpan startsIn, bool withLink = true)
    {
        var (instructor, profileId) = await CreateInstructorAsync();
        var (courseId, slug) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, profileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId, daysAhead: 2);

        if (withLink)
        {
            var set = await SendAsync(HttpMethod.Put, $"/api/live/instructor/sessions/{sessionId}/meeting-link", instructor, new { meetUrl = RoomUrl });
            Assert.Equal(HttpStatusCode.OK, set.Status);
        }

        if (startsIn != TimeSpan.FromDays(2))
        {
            await MoveSessionAsync(sessionId, DateTime.UtcNow + startsIn);
        }

        return new Scene(instructor, profileId, courseId, slug, sessionId);
    }

    /// <summary>Rewrites a session's window directly (the domain refuses to schedule into the past) — used to put a session inside the join window, or after its end.</summary>
    private async Task MoveSessionAsync(Guid sessionId, DateTime startsAtUtc)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var session = await db.CourseLiveSessions().SingleAsync(s => s.Id == sessionId);
        db.Entry(session).Property(s => s.StartsAtUtc).CurrentValue = startsAtUtc;
        db.Entry(session).Property(s => s.EndsAtUtc).CurrentValue = startsAtUtc.AddHours(2);
        await db.SaveChangesAsync();
    }

    private async Task EnrollAsync(Actor learner, Guid courseId, Action<ENROLLMENT>? shape = null, DateTime? expiresAtUtc = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var enrollment = ENROLLMENT.Create(learner.UserId, courseId, null, EnrollmentSource.Purchase, expiresAtUtc, clock);
        shape?.Invoke(enrollment);
        db.Enrollments().Add(enrollment);
        await db.SaveChangesAsync();
    }

    private async Task<Actor> EnrolledLearnerAsync(Guid courseId)
    {
        var learner = await CreateActorAsync(ROLE.LearnerName);
        await EnrollAsync(learner, courseId);
        return learner;
    }

    private async Task<Reply> SendAsync(HttpMethod method, string uri, Actor? actor, object? json = null, HttpClient? client = null)
    {
        using var request = LiveIntegrationSupport.Authorized(method, uri, actor?.Token);
        request.Headers.UserAgent.ParseAdd(UserAgent);
        if (json is not null)
        {
            request.Content = System.Net.Http.Json.JsonContent.Create(json);
        }

        using var response = await (client ?? _client).SendAsync(request);
        var body = await response.Content.ReadAsStringAsync();

        return new Reply(
            response.StatusCode,
            body,
            response.Headers.CacheControl?.NoStore == true,
            response.Headers.Pragma.Any(p => string.Equals(p.Name, "no-cache", StringComparison.OrdinalIgnoreCase)),
            response.Headers.RetryAfter?.Delta,
            response.Content.Headers.ContentType?.ToString(),
            response.Content.Headers.ContentDisposition?.ToString() ?? (response.Headers.TryGetValues("Content-Disposition", out var values) ? values.FirstOrDefault() : null));
    }

    private Task<Reply> JoinAsync(Actor? actor, Guid sessionId, HttpClient? client = null) =>
        SendAsync(HttpMethod.Post, $"/api/live/sessions/{sessionId}/join", actor, client: client);

    private async Task<IReadOnlyList<SESSION_JOIN_LOG>> JoinLogsAsync(Guid sessionId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        return await db.SessionJoinLogs().AsNoTracking().Where(l => l.SESSION_ID == sessionId).OrderBy(l => l.JOINED_AT_UTC).ToListAsync();
    }

    // ---- The happy path ------------------------------------------------------------------------------------

    [Fact]
    public async Task Join_EnrolledLearnerInsideTheWindow_Returns200WithTheLink_NoStore_AndTheLogRowIsAlreadyThere()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromMinutes(5));
        var learner = await EnrolledLearnerAsync(scene.CourseId);

        var reply = await JoinAsync(learner, scene.SessionId);

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.True(reply.NoStore, "Cache-Control must be no-store");
        Assert.True(reply.PragmaNoCache, "Pragma must be no-cache");
        using var json = reply.Json;
        Assert.Equal(RoomUrl, json.RootElement.GetProperty("meetUrl").GetString());
        Assert.Equal(scene.SessionId, json.RootElement.GetProperty("sessionId").GetGuid());
        Assert.EndsWith("Z", json.RootElement.GetProperty("startsAtUtc").GetString());
        Assert.EndsWith("Z", json.RootElement.GetProperty("endsAtUtc").GetString());
        Assert.EndsWith("Z", json.RootElement.GetProperty("serverTimeUtc").GetString());

        // The response is only produced after the commit: by the time the caller holds the link, the evidence row exists.
        var row = Assert.Single(await JoinLogsAsync(scene.SessionId));
        Assert.Equal(learner.UserId, row.USER_ID);
        Assert.Equal(scene.CourseId, row.COURSE_ID);
        Assert.Equal(LiveParticipantRole.Learner, row.ROLE);
        Assert.Equal(learner.AuthSessionId, row.AUTH_SESSION_ID);
        Assert.Equal(UserAgent, row.USER_AGENT);
        Assert.Equal(DateTimeKind.Utc, row.JOINED_AT_UTC.Kind);
        Assert.InRange(row.JOINED_AT_UTC, DateTime.UtcNow.AddMinutes(-2), DateTime.UtcNow.AddSeconds(5));
    }

    [Fact]
    public async Task Join_CalledAgain_AppendsAnotherLogRow()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromMinutes(5));
        var learner = await EnrolledLearnerAsync(scene.CourseId);

        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(learner, scene.SessionId)).Status);
        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(learner, scene.SessionId)).Status);

        var rows = await JoinLogsAsync(scene.SessionId);
        Assert.Equal(2, rows.Count);
        Assert.All(rows, r => Assert.Equal(learner.UserId, r.USER_ID));
    }

    [Fact]
    public async Task Join_TheOwningInstructor_CanEnterDaysBeforeTheWindow_AndIsLoggedAsInstructor()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromDays(2));

        var reply = await JoinAsync(scene.Instructor, scene.SessionId);

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        using var json = reply.Json;
        Assert.Equal(RoomUrl, json.RootElement.GetProperty("meetUrl").GetString());
        Assert.Equal(LiveParticipantRole.Instructor, Assert.Single(await JoinLogsAsync(scene.SessionId)).ROLE);
    }

    // ---- State refusals ----------------------------------------------------------------------------------------

    [Fact]
    public async Task Join_WindowNotOpenYet_Is409_WithTheOpeningTime_AndNoLog()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromDays(2));
        var learner = await EnrolledLearnerAsync(scene.CourseId);

        var reply = await JoinAsync(learner, scene.SessionId);

        Assert.Equal(HttpStatusCode.Conflict, reply.Status);
        Assert.True(reply.NoStore);
        using var json = reply.Json;
        Assert.Equal("live.window_not_open", json.RootElement.GetProperty("reason").GetString());
        Assert.Equal("conflict", json.RootElement.GetProperty("errorCode").GetString());
        Assert.EndsWith("Z", json.RootElement.GetProperty("opensAtUtc").GetString());
        Assert.EndsWith("Z", json.RootElement.GetProperty("serverTimeUtc").GetString());
        Assert.DoesNotContain(RoomSecret, reply.Body);
        Assert.Empty(await JoinLogsAsync(scene.SessionId));
    }

    [Fact]
    public async Task Join_EndedSession_Is409_WithTheCourseSlug_AndNoLog()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromHours(-5)); // started five hours ago, lasted two
        var learner = await EnrolledLearnerAsync(scene.CourseId);

        var reply = await JoinAsync(learner, scene.SessionId);

        Assert.Equal(HttpStatusCode.Conflict, reply.Status);
        using var json = reply.Json;
        Assert.Equal("live.session_ended", json.RootElement.GetProperty("reason").GetString());
        Assert.Equal(scene.CourseSlug, json.RootElement.GetProperty("courseSlug").GetString());
        Assert.DoesNotContain(RoomSecret, reply.Body);
        Assert.Empty(await JoinLogsAsync(scene.SessionId));
    }

    [Fact]
    public async Task Join_CancelledSession_Is409_AndNoLog()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromDays(2));
        var learner = await EnrolledLearnerAsync(scene.CourseId);
        var cancel = await SendAsync(
            HttpMethod.Post, $"/api/catalog/instructor/courses/{scene.CourseId}/live-sessions/{scene.SessionId}/cancel", scene.Instructor, new { reason = "ผู้สอนไม่สะดวก" });
        Assert.Equal(HttpStatusCode.OK, cancel.Status);

        var reply = await JoinAsync(learner, scene.SessionId);

        Assert.Equal(HttpStatusCode.Conflict, reply.Status);
        using var json = reply.Json;
        Assert.Equal("live.session_cancelled", json.RootElement.GetProperty("reason").GetString());
        Assert.Empty(await JoinLogsAsync(scene.SessionId));
    }

    [Fact]
    public async Task Join_RoomNotReady_Is503_WithRetryAfter30_NoStore_AndNoLog()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromMinutes(5), withLink: false);
        var learner = await EnrolledLearnerAsync(scene.CourseId);

        var reply = await JoinAsync(learner, scene.SessionId);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, reply.Status);
        Assert.Equal(TimeSpan.FromSeconds(30), reply.RetryAfter);
        Assert.True(reply.NoStore);
        Assert.True(reply.PragmaNoCache);
        using var json = reply.Json;
        Assert.Equal("live.meeting_not_ready", json.RootElement.GetProperty("reason").GetString());
        Assert.Empty(await JoinLogsAsync(scene.SessionId));
    }

    // ---- IDOR: no existence oracle ---------------------------------------------------------------------------------------

    [Fact]
    public async Task LearnerEndpoints_EveryNonEntitledCaller_GetsTheByteIdentical404_OfAnUnknownId()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromMinutes(5));
        var entitled = await EnrolledLearnerAsync(scene.CourseId);

        var stranger = await CreateActorAsync(ROLE.LearnerName);
        var expired = await CreateActorAsync(ROLE.LearnerName);
        await EnrollAsync(expired, scene.CourseId, e => e.Expire());
        var revoked = await CreateActorAsync(ROLE.LearnerName);
        await EnrollAsync(revoked, scene.CourseId, e => e.Revoke());
        var lapsedByDate = await CreateActorAsync(ROLE.LearnerName);
        await EnrollAsync(lapsedByDate, scene.CourseId, expiresAtUtc: DateTime.UtcNow.AddDays(-1));
        var (otherInstructor, _) = await CreateInstructorAsync();
        var admin = await CreateActorAsync(ROLE.AdminName);
        var prober = await CreateActorAsync(ROLE.LearnerName);

        var unknownSession = Guid.NewGuid();
        var unknownCourse = Guid.NewGuid();

        // Positive controls: the entitled learner really is let in, so the 404s below are about entitlement and nothing else.
        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(entitled, scene.SessionId)).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, $"/api/live/sessions/{scene.SessionId}/calendar.ics", entitled)).Status);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, $"/api/live/courses/{scene.CourseId}/my-sessions", entitled)).Status);

        var baselineJoin = await JoinAsync(prober, unknownSession);
        var baselineIcs = await SendAsync(HttpMethod.Get, $"/api/live/sessions/{unknownSession}/calendar.ics", prober);
        var baselineMine = await SendAsync(HttpMethod.Get, $"/api/live/courses/{unknownCourse}/my-sessions", prober);
        Assert.Equal(HttpStatusCode.NotFound, baselineJoin.Status);
        Assert.Equal(HttpStatusCode.NotFound, baselineIcs.Status);
        Assert.Equal(HttpStatusCode.NotFound, baselineMine.Status);

        foreach (var caller in new[] { stranger, expired, revoked, lapsedByDate, otherInstructor, admin })
        {
            var join = await JoinAsync(caller, scene.SessionId);
            var ics = await SendAsync(HttpMethod.Get, $"/api/live/sessions/{scene.SessionId}/calendar.ics", caller);
            var mine = await SendAsync(HttpMethod.Get, $"/api/live/courses/{scene.CourseId}/my-sessions", caller);

            Assert.Equal(HttpStatusCode.NotFound, join.Status);
            Assert.Equal(HttpStatusCode.NotFound, ics.Status);
            Assert.Equal(HttpStatusCode.NotFound, mine.Status);

            // Same status, same title, same errorCode, same reason — only the trace id may differ.
            Assert.Equal(baselineJoin.WithoutTraceId(), join.WithoutTraceId());
            Assert.Equal(baselineIcs.WithoutTraceId(), ics.WithoutTraceId());
            Assert.Equal(baselineMine.WithoutTraceId(), mine.WithoutTraceId());
            Assert.True(join.NoStore, "even the refusals are no-store");

            Assert.DoesNotContain(RoomSecret, join.Body);
            Assert.DoesNotContain(RoomSecret, ics.Body);
        }

        // Only the entitled learner has a log row — not one refusal wrote anything.
        Assert.Equal(entitled.UserId, Assert.Single(await JoinLogsAsync(scene.SessionId)).USER_ID);
    }

    [Fact]
    public async Task LearnerEndpoints_Anonymous_Are401()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromMinutes(5));

        Assert.Equal(HttpStatusCode.Unauthorized, (await JoinAsync(null, scene.SessionId)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, $"/api/live/sessions/{scene.SessionId}/calendar.ics", null)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, $"/api/live/courses/{scene.CourseId}/my-sessions", null)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, "/api/live/me/sessions/upcoming", null)).Status);
        Assert.Empty(await JoinLogsAsync(scene.SessionId));
    }

    // ---- Instructor endpoints: ownership --------------------------------------------------------------------------------------

    [Fact]
    public async Task InstructorEndpoints_OwnershipMatrix_OwnerOk_OthersForbidden_UnknownNotFound_AnonymousUnauthorized()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromDays(2));
        var learner = await EnrolledLearnerAsync(scene.CourseId);
        var (otherInstructor, _) = await CreateInstructorAsync();
        var admin = await CreateActorAsync(ROLE.AdminName);

        string[] urls =
        [
            $"/api/live/instructor/sessions/{scene.SessionId}",
            $"/api/live/instructor/sessions/{scene.SessionId}/roster",
            $"/api/catalog/instructor/courses/{scene.CourseId}/live-schedule",
        ];

        foreach (var url in urls)
        {
            Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, url, scene.Instructor)).Status);

            // Not the owner: another instructor and an administrator (no bypass) are 403; a learner never passes the InstructorOnly policy (403).
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, url, otherInstructor)).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, url, admin)).Status);
            Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, url, learner)).Status);
            Assert.Equal(HttpStatusCode.Unauthorized, (await SendAsync(HttpMethod.Get, url, null)).Status);
        }

        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/live/instructor/sessions/{Guid.NewGuid()}", scene.Instructor)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/live/instructor/sessions/{Guid.NewGuid()}/roster", scene.Instructor)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await SendAsync(HttpMethod.Get, $"/api/catalog/instructor/courses/{Guid.NewGuid()}/live-schedule", scene.Instructor)).Status);
    }

    [Fact]
    public async Task InstructorSessionList_ShowsOnlyTheCallersOwnSessions_AndAnotherInstructorsCourseFilterMatchesNothing()
    {
        var mine = await CreateSceneAsync(TimeSpan.FromDays(2));
        var theirs = await CreateSceneAsync(TimeSpan.FromDays(2));

        var all = await SendAsync(HttpMethod.Get, "/api/live/instructor/sessions?scope=all", mine.Instructor);
        var filteredToTheirs = await SendAsync(HttpMethod.Get, $"/api/live/instructor/sessions?scope=all&courseId={theirs.CourseId}", mine.Instructor);

        Assert.Equal(HttpStatusCode.OK, all.Status);
        using (var json = all.Json)
        {
            var ids = json.RootElement.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("sessionId").GetGuid()).ToArray();
            Assert.Contains(mine.SessionId, ids);
            Assert.DoesNotContain(theirs.SessionId, ids);
        }

        using var filtered = filteredToTheirs.Json;
        Assert.Empty(filtered.RootElement.GetProperty("items").EnumerateArray());
        Assert.Equal(0, filtered.RootElement.GetProperty("totalCount").GetInt32());
    }

    [Fact]
    public async Task InstructorSessionList_PagingIsBounded_AndAnInvalidScopeIs400()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromDays(2));

        var big = await SendAsync(HttpMethod.Get, "/api/live/instructor/sessions?scope=upcoming&pageSize=1000", scene.Instructor);
        var bad = await SendAsync(HttpMethod.Get, "/api/live/instructor/sessions?scope=sideways", scene.Instructor);

        using (var json = big.Json)
        {
            Assert.Equal(50, json.RootElement.GetProperty("pageSize").GetInt32());
        }

        Assert.Equal(HttpStatusCode.BadRequest, bad.Status);
    }

    // ---- The link leaves the system through exactly two doors -----------------------------------------------------------------

    [Fact]
    public async Task TheRoomLink_AppearsOnlyInTheJoinResponseAndTheOwnersSessionDetail_NowhereElse()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromMinutes(5));
        var learner = await EnrolledLearnerAsync(scene.CourseId);

        // Publish the course so the public read models include it (the domain's own invariant for a Live course: at least one future scheduled session — the
        // session starts in five minutes, so it has one).
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();
            var course = await db.Courses().Include(c => c.LiveSessions).SingleAsync(c => c.Id == scene.CourseId);
            course.Publish(clock);
            await db.SaveChangesAsync();
        }

        string[] guarded =
        [
            $"/api/live/courses/{scene.CourseId}/my-sessions",
            "/api/live/me/sessions/upcoming",
            $"/api/live/sessions/{scene.SessionId}/calendar.ics",
        ];

        foreach (var url in guarded)
        {
            var reply = await SendAsync(HttpMethod.Get, url, learner);
            Assert.Equal(HttpStatusCode.OK, reply.Status);
            Assert.DoesNotContain(RoomSecret, reply.Body);
            Assert.DoesNotContain("zoom.us", reply.Body, StringComparison.OrdinalIgnoreCase);

            if (!url.EndsWith(".ics", StringComparison.Ordinal))
            {
                using var json = reply.Json;
                LiveIntegrationSupport.AssertNoRoomUrl(json, RoomSecret, "zoom.us");
            }
        }

        string[] instructorOnly =
        [
            "/api/live/instructor/sessions?scope=all",
            $"/api/live/instructor/sessions/{scene.SessionId}/roster",
            $"/api/live/instructor/courses/{scene.CourseId}/meetings",
            $"/api/catalog/instructor/courses/{scene.CourseId}/live-schedule",
        ];

        foreach (var url in instructorOnly)
        {
            var reply = await SendAsync(HttpMethod.Get, url, scene.Instructor);
            Assert.Equal(HttpStatusCode.OK, reply.Status);
            using var json = reply.Json;
            LiveIntegrationSupport.AssertNoRoomUrl(json, RoomSecret, "zoom.us");
        }

        // Public course detail and search never mention it either (they need no login).
        var detail = await SendAsync(HttpMethod.Get, $"/api/catalog/courses/{scene.CourseSlug}", null);
        var search = await SendAsync(HttpMethod.Get, "/api/catalog/courses/search?q=" + Uri.EscapeDataString("คอร์สสดสำหรับทดสอบ"), null);
        Assert.Equal(HttpStatusCode.OK, detail.Status); // a control: the walk below is over a real, published course
        Assert.Equal(HttpStatusCode.OK, search.Status);
        Assert.Contains("liveSchedule", detail.Body);
        Assert.DoesNotContain(RoomSecret, detail.Body);
        Assert.DoesNotContain(RoomSecret, search.Body);
        Assert.DoesNotContain("zoom.us", search.Body, StringComparison.OrdinalIgnoreCase);

        // The two doors that DO carry it.
        var join = await JoinAsync(learner, scene.SessionId);
        using (var joined = join.Json)
        {
            Assert.Equal(RoomUrl, joined.RootElement.GetProperty("meetUrl").GetString());
        }

        var ownerDetail = await SendAsync(HttpMethod.Get, $"/api/live/instructor/sessions/{scene.SessionId}", scene.Instructor);
        Assert.True(ownerDetail.NoStore);
        using var ownerJson = ownerDetail.Json;
        Assert.Equal(RoomUrl, ownerJson.RootElement.GetProperty("meetUrl").GetString());
    }

    [Fact]
    public async Task TheRoomLink_NeverReachesTheServicesLogs()
    {
        var joinLog = new CapturingLogger<SessionJoinService>();
        var meetingLog = new CapturingLogger<SessionMeetingService>();
        using var factory = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
        {
            services.AddSingleton<ILogger<SessionJoinService>>(joinLog);
            services.AddSingleton<ILogger<SessionMeetingService>>(meetingLog);
        }));
        using var client = factory.CreateClient();

        var scene = await CreateSceneAsync(TimeSpan.FromMinutes(5));
        var learner = await EnrolledLearnerAsync(scene.CourseId);
        var stranger = await CreateActorAsync(ROLE.LearnerName);

        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(learner, scene.SessionId, client)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await JoinAsync(stranger, scene.SessionId, client)).Status);

        var logged = string.Join('\n', joinLog.Messages.Concat(meetingLog.Messages));
        Assert.Contains("live.join.denied", logged); // the refusal was logged...
        Assert.DoesNotContain(RoomSecret, logged); // ...but never with the link
        Assert.DoesNotContain("zoom.us", logged, StringComparison.OrdinalIgnoreCase);
    }

    // ---- "No log, no link" ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Join_WhenTheLogCannotBeCommitted_NoLinkIsReturned()
    {
        using var failing = _factory.WithWebHostBuilder(builder => builder.ConfigureTestServices(services =>
            services.AddScoped<ISessionJoinLogRepository, FailingJoinLogRepository>()));
        using var client = failing.CreateClient();

        var scene = await CreateSceneAsync(TimeSpan.FromMinutes(5));
        var learner = await EnrolledLearnerAsync(scene.CourseId);

        Reply? reply = null;
        var failure = await Record.ExceptionAsync(async () => reply = await JoinAsync(learner, scene.SessionId, client));

        // Two acceptable shapes, and only these: the host answered 500 without the link, or the failure reached the test server as the original exception. Anything
        // else — above all a 200 carrying the link — fails here.
        if (reply is not null)
        {
            Assert.Equal(HttpStatusCode.InternalServerError, reply.Status);
            Assert.DoesNotContain(RoomSecret, reply.Body);
            Assert.DoesNotContain("zoom.us", reply.Body, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.NotNull(failure);
            Assert.Contains("simulated outage", failure.ToString());
            Assert.DoesNotContain(RoomSecret, failure.ToString());
        }

        Assert.Empty(await JoinLogsAsync(scene.SessionId));
    }

    // ---- Rate limit ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Join_TheSeventhRequestInAMinuteIs429_AndAnotherUserIsUnaffected()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromMinutes(5));
        var busy = await EnrolledLearnerAsync(scene.CourseId);
        var calm = await EnrolledLearnerAsync(scene.CourseId);

        for (var i = 0; i < 6; i++)
        {
            Assert.Equal(HttpStatusCode.OK, (await JoinAsync(busy, scene.SessionId)).Status);
        }

        var limited = await JoinAsync(busy, scene.SessionId);

        Assert.Equal(HttpStatusCode.TooManyRequests, limited.Status);
        Assert.DoesNotContain(RoomSecret, limited.Body);

        // Partitioned per user: a different learner joining at the same moment is not starved by the first one.
        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(calm, scene.SessionId)).Status);
    }

    // ---- calendar.ics -------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Calendar_ForAnEntitledLearner_IsATextCalendarAttachment_WhoseUidAndSequenceMatchTheInvitationEmails_AndHasNoRoomLink()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromDays(2));
        var learner = await EnrolledLearnerAsync(scene.CourseId);

        var reply = await SendAsync(HttpMethod.Get, $"/api/live/sessions/{scene.SessionId}/calendar.ics", learner);

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        Assert.True(reply.NoStore);
        Assert.Contains("text/calendar", reply.ContentType);
        Assert.Contains("method=PUBLISH", reply.ContentType);
        Assert.Contains($"live-{scene.SessionId:N}.ics", reply.ContentDisposition);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var meeting = await db.SessionMeetings().AsNoTracking().SingleAsync(m => m.SESSION_ID == scene.SessionId);
        var options = scope.ServiceProvider.GetRequiredService<IOptions<LiveOptions>>().Value;
        var host = new Uri(options.GetNormalizedPublicBaseUrl()).IdnHost;

        var ics = reply.Body.Replace("\r\n ", string.Empty, StringComparison.Ordinal);
        Assert.Contains("METHOD:PUBLISH", ics);
        Assert.Contains($"UID:{IcsCalendarBuilder.BuildUid(scene.SessionId, host)}", ics);
        Assert.Contains($"SEQUENCE:{meeting.ICS_SEQUENCE}", ics);
        Assert.Contains($"URL:{options.GetNormalizedPublicBaseUrl()}/live/{scene.SessionId}/join", ics);
        Assert.DoesNotContain(RoomSecret, ics);
        Assert.DoesNotContain("zoom.us", ics, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Calendar_CancelledOrEndedSession_Is409()
    {
        var cancelled = await CreateSceneAsync(TimeSpan.FromDays(2));
        var learnerA = await EnrolledLearnerAsync(cancelled.CourseId);
        await SendAsync(HttpMethod.Post, $"/api/catalog/instructor/courses/{cancelled.CourseId}/live-sessions/{cancelled.SessionId}/cancel", cancelled.Instructor, new { reason = "x" });

        var ended = await CreateSceneAsync(TimeSpan.FromHours(-5));
        var learnerB = await EnrolledLearnerAsync(ended.CourseId);

        var first = await SendAsync(HttpMethod.Get, $"/api/live/sessions/{cancelled.SessionId}/calendar.ics", learnerA);
        var second = await SendAsync(HttpMethod.Get, $"/api/live/sessions/{ended.SessionId}/calendar.ics", learnerB);

        Assert.Equal(HttpStatusCode.Conflict, first.Status);
        using (var json = first.Json)
        {
            Assert.Equal("live.session_cancelled", json.RootElement.GetProperty("reason").GetString());
        }

        Assert.Equal(HttpStatusCode.Conflict, second.Status);
        using var endedJson = second.Json;
        Assert.Equal("live.session_ended", endedJson.RootElement.GetProperty("reason").GetString());
    }

    // ---- my-sessions / upcoming ---------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task MySessions_ForAnEnrolledLearner_HasServerComputedStates_AndFlipsHasAttendedAfterTheFirstJoin()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromMinutes(5));
        var learner = await EnrolledLearnerAsync(scene.CourseId);

        var before = await SendAsync(HttpMethod.Get, $"/api/live/courses/{scene.CourseId}/my-sessions", learner);
        Assert.Equal(HttpStatusCode.OK, before.Status);
        Assert.True(before.NoStore);
        using (var json = before.Json)
        {
            var root = json.RootElement;
            Assert.Equal(scene.CourseId, root.GetProperty("courseId").GetGuid());
            Assert.Equal(scene.CourseSlug, root.GetProperty("courseSlug").GetString());
            Assert.Equal("Asia/Bangkok", root.GetProperty("timezone").GetString());
            Assert.EndsWith("Z", root.GetProperty("serverTimeUtc").GetString());
            Assert.False(root.GetProperty("hasAttendedAnySession").GetBoolean());

            var session = Assert.Single(root.GetProperty("sessions").EnumerateArray());
            Assert.Equal(scene.SessionId, session.GetProperty("id").GetGuid());
            Assert.Equal("Live", session.GetProperty("displayState").GetString());
            Assert.True(session.GetProperty("canJoin").GetBoolean());
            Assert.True(session.GetProperty("roomReady").GetBoolean());
            Assert.Equal("Scheduled", session.GetProperty("status").GetString());
            Assert.EndsWith("Z", session.GetProperty("joinOpensAtUtc").GetString());
        }

        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(learner, scene.SessionId)).Status);

        var after = await SendAsync(HttpMethod.Get, $"/api/live/courses/{scene.CourseId}/my-sessions", learner);
        using var afterJson = after.Json;
        Assert.True(afterJson.RootElement.GetProperty("hasAttendedAnySession").GetBoolean());
    }

    [Fact]
    public async Task Upcoming_ListsTheNextClassesOfEveryActiveEnrollment_SoonestFirst_AndHonoursTheLimit()
    {
        var soon = await CreateSceneAsync(TimeSpan.FromMinutes(5));
        var later = await CreateSceneAsync(TimeSpan.FromDays(2));
        var learner = await CreateActorAsync(ROLE.LearnerName);
        await EnrollAsync(learner, soon.CourseId);
        await EnrollAsync(learner, later.CourseId);
        var unrelated = await CreateSceneAsync(TimeSpan.FromDays(2)); // not enrolled

        var reply = await SendAsync(HttpMethod.Get, "/api/live/me/sessions/upcoming?limit=10", learner);
        Assert.Equal(HttpStatusCode.OK, reply.Status);
        using (var json = reply.Json)
        {
            var items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal([soon.SessionId, later.SessionId], items.Select(i => i.GetProperty("sessionId").GetGuid()).ToArray());
            Assert.DoesNotContain(unrelated.SessionId, items.Select(i => i.GetProperty("sessionId").GetGuid()));
            Assert.Equal("Live", items[0].GetProperty("displayState").GetString());
            Assert.True(items[0].GetProperty("canJoin").GetBoolean());
            Assert.Equal("Upcoming", items[1].GetProperty("displayState").GetString());
            Assert.EndsWith("Z", json.RootElement.GetProperty("serverTimeUtc").GetString());
        }

        var one = await SendAsync(HttpMethod.Get, "/api/live/me/sessions/upcoming?limit=1", learner);
        using var oneJson = one.Json;
        Assert.Single(oneJson.RootElement.GetProperty("items").EnumerateArray());
    }

    // ---- Roster -----------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Roster_ShowsMaskedEmailsOnly_FiltersJoinedAndNotJoined_AndPages()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromMinutes(5));
        var joinedLearner = await EnrolledLearnerAsync(scene.CourseId);
        var quietLearner = await EnrolledLearnerAsync(scene.CourseId);
        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(joinedLearner, scene.SessionId)).Status);
        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(joinedLearner, scene.SessionId)).Status);
        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(scene.Instructor, scene.SessionId)).Status); // the instructor is never on the roster

        var all = await SendAsync(HttpMethod.Get, $"/api/live/instructor/sessions/{scene.SessionId}/roster", scene.Instructor);
        var joined = await SendAsync(HttpMethod.Get, $"/api/live/instructor/sessions/{scene.SessionId}/roster?filter=joined", scene.Instructor);
        var notJoined = await SendAsync(HttpMethod.Get, $"/api/live/instructor/sessions/{scene.SessionId}/roster?filter=notJoined", scene.Instructor);
        var paged = await SendAsync(HttpMethod.Get, $"/api/live/instructor/sessions/{scene.SessionId}/roster?pageSize=1&page=2", scene.Instructor);

        Assert.True(all.NoStore);
        using (var json = all.Json)
        {
            var items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(2, json.RootElement.GetProperty("totalCount").GetInt32());
            Assert.DoesNotContain(scene.Instructor.UserId, items.Select(i => i.GetProperty("userId").GetGuid()));

            var row = items.Single(i => i.GetProperty("userId").GetGuid() == joinedLearner.UserId);
            Assert.True(row.GetProperty("joined").GetBoolean());
            Assert.Equal(2, row.GetProperty("joinCount").GetInt32());
            Assert.EndsWith("Z", row.GetProperty("firstJoinedAtUtc").GetString());
            Assert.Contains("***", row.GetProperty("emailMasked").GetString());
        }

        // The full address of either learner appears nowhere in the response.
        Assert.DoesNotContain(joinedLearner.Email, all.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(quietLearner.Email, all.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("@example.test", all.Body, StringComparison.OrdinalIgnoreCase);

        using (var json = joined.Json)
        {
            Assert.Equal(joinedLearner.UserId, Assert.Single(json.RootElement.GetProperty("items").EnumerateArray()).GetProperty("userId").GetGuid());
        }

        using (var json = notJoined.Json)
        {
            Assert.Equal(quietLearner.UserId, Assert.Single(json.RootElement.GetProperty("items").EnumerateArray()).GetProperty("userId").GetGuid());
        }

        using var pagedJson = paged.Json;
        Assert.Equal(2, pagedJson.RootElement.GetProperty("page").GetInt32());
        Assert.Equal(1, pagedJson.RootElement.GetProperty("pageSize").GetInt32());
        Assert.Equal(2, pagedJson.RootElement.GetProperty("totalPages").GetInt32());
        Assert.Single(pagedJson.RootElement.GetProperty("items").EnumerateArray());
    }

    // ---- Instructor detail and list numbers ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task InstructorDetail_CarriesTheLink_TheCountsAndNoStore()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromMinutes(5));
        var learner = await EnrolledLearnerAsync(scene.CourseId);
        await EnrolledLearnerAsync(scene.CourseId);
        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(learner, scene.SessionId)).Status);

        var detail = await SendAsync(HttpMethod.Get, $"/api/live/instructor/sessions/{scene.SessionId}", scene.Instructor);
        var list = await SendAsync(HttpMethod.Get, "/api/live/instructor/sessions?scope=upcoming", scene.Instructor);

        Assert.Equal(HttpStatusCode.OK, detail.Status);
        Assert.True(detail.NoStore);
        using (var json = detail.Json)
        {
            var root = json.RootElement;
            Assert.Equal(RoomUrl, root.GetProperty("meetUrl").GetString());
            Assert.Equal(2, root.GetProperty("enrolledCount").GetInt32());
            Assert.Equal(1, root.GetProperty("joinedLearners").GetInt32());
            Assert.Equal(scene.CourseSlug, root.GetProperty("courseSlug").GetString());
            Assert.Equal("Live", root.GetProperty("displayState").GetString());
            Assert.True(root.GetProperty("meeting").GetProperty("isUsable").GetBoolean());
            Assert.Equal("None", root.GetProperty("meeting").GetProperty("needsAction").GetString());
        }

        using var listJson = list.Json;
        var item = listJson.RootElement.GetProperty("items").EnumerateArray().Single(i => i.GetProperty("sessionId").GetGuid() == scene.SessionId);
        Assert.Equal(1, item.GetProperty("joinedLearners").GetInt32());
    }

    // ---- Catalog: live-schedule -------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task CatalogLiveSchedule_ReturnsTheSettingsAndEverySessionInEveryStatus_WithoutALink()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromDays(2));
        var secondId = await LiveIntegrationSupport.CreateSessionAsync(_client, scene.Instructor.Token, scene.CourseId, daysAhead: 5);
        var cancel = await SendAsync(
            HttpMethod.Post, $"/api/catalog/instructor/courses/{scene.CourseId}/live-sessions/{secondId}/cancel", scene.Instructor, new { reason = "เลื่อน" });
        Assert.Equal(HttpStatusCode.OK, cancel.Status);

        var reply = await SendAsync(HttpMethod.Get, $"/api/catalog/instructor/courses/{scene.CourseId}/live-schedule", scene.Instructor);

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        using var json = reply.Json;
        var root = json.RootElement;
        Assert.Equal(scene.CourseId, root.GetProperty("courseId").GetGuid());
        Assert.Equal("Live", root.GetProperty("deliveryFormat").GetString());
        Assert.Equal("Draft", root.GetProperty("status").GetString());
        Assert.False(root.GetProperty("googleAttendeeSyncEnabled").GetBoolean());
        Assert.Equal(0, root.GetProperty("seatsUsed").GetInt32());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("maxSeats").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("enrollmentDeadlineUtc").ValueKind);

        var sessions = root.GetProperty("sessions").EnumerateArray().ToArray();
        Assert.Equal(2, sessions.Length);
        Assert.Equal([scene.SessionId, secondId], sessions.Select(s => s.GetProperty("id").GetGuid()).ToArray()); // by start time
        Assert.Equal(["Scheduled", "Cancelled"], sessions.Select(s => s.GetProperty("status").GetString()!).ToArray());
        Assert.Equal("เลื่อน", sessions[1].GetProperty("cancelReason").GetString());
        Assert.All(sessions, s => Assert.EndsWith("Z", s.GetProperty("startsAtUtc").GetString()));

        LiveIntegrationSupport.AssertNoRoomUrl(json, RoomSecret, "zoom.us");
    }

    // ---- Contracts other modules consume ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task AttendanceReader_CountsOnlyLearners_ForTheRefundRuleAndTheDashboard()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromMinutes(5));
        var learner = await EnrolledLearnerAsync(scene.CourseId);
        var invitedOnly = await EnrolledLearnerAsync(scene.CourseId);
        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(learner, scene.SessionId)).Status);
        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(learner, scene.SessionId)).Status); // twice: still one distinct learner
        Assert.Equal(HttpStatusCode.OK, (await JoinAsync(scene.Instructor, scene.SessionId)).Status);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var invited = SESSION_INVITE.Create(scene.SessionId, invitedOnly.UserId, LiveParticipantRole.Learner, clock);
        invited.MarkInvited(0, clock);
        var pending = SESSION_INVITE.Create(scene.SessionId, learner.UserId, LiveParticipantRole.Learner, clock); // Pending: not "expected"
        var instructorInvite = SESSION_INVITE.Create(scene.SessionId, scene.Instructor.UserId, LiveParticipantRole.Instructor, clock);
        instructorInvite.MarkInvited(0, clock); // an Invited INSTRUCTOR is not an expected learner
        db.SessionInvites().AddRange(invited, pending, instructorInvite);
        await db.SaveChangesAsync();

        var reader = scope.ServiceProvider.GetRequiredService<ILiveAttendanceReader>();

        var learnerCourses = await reader.GetCourseIdsAttendedAsync(learner.UserId, [scene.CourseId, Guid.NewGuid()], CancellationToken.None);
        var instructorCourses = await reader.GetCourseIdsAttendedAsync(scene.Instructor.UserId, [scene.CourseId], CancellationToken.None);
        var quietCourses = await reader.GetCourseIdsAttendedAsync(invitedOnly.UserId, [scene.CourseId], CancellationToken.None);
        var stats = await reader.GetSessionStatsAsync([scene.SessionId, Guid.NewGuid()], CancellationToken.None);

        Assert.Equal([scene.CourseId], learnerCourses.ToArray());
        Assert.Empty(instructorCourses); // an instructor entering their own room is not "attending" for the refund rule
        Assert.Empty(quietCourses);

        var real = stats[scene.SessionId];
        Assert.Equal(1, real.ExpectedLearners);
        Assert.Equal(1, real.JoinedLearners);
        Assert.True(real.MeetingUsable);

        var unknown = stats.Values.Single(s => s.SessionId != scene.SessionId);
        Assert.Equal(0, unknown.ExpectedLearners);
        Assert.Equal(0, unknown.JoinedLearners);
        Assert.False(unknown.MeetingUsable);
    }

    [Fact]
    public async Task Learning_GetActiveEnrolledCourseIds_ReturnsOnlyActiveUnexpiredCourses()
    {
        var learner = await CreateActorAsync(ROLE.LearnerName);
        var active = Guid.NewGuid();
        var withFutureExpiry = Guid.NewGuid();
        var expired = Guid.NewGuid();
        var revoked = Guid.NewGuid();
        var lapsedByDate = Guid.NewGuid();
        await EnrollAsync(learner, active);
        await EnrollAsync(learner, withFutureExpiry, expiresAtUtc: DateTime.UtcNow.AddDays(10));
        await EnrollAsync(learner, expired, e => e.Expire());
        await EnrollAsync(learner, revoked, e => e.Revoke());
        await EnrollAsync(learner, lapsedByDate, expiresAtUtc: DateTime.UtcNow.AddMinutes(-1));

        await using var scope = _factory.Services.CreateAsyncScope();
        var ids = await scope.ServiceProvider.GetRequiredService<ILearningAccessContract>().GetActiveEnrolledCourseIdsAsync(learner.UserId, CancellationToken.None);

        Assert.Equal(new[] { active, withFutureExpiry }.Order(), ids.Order());
    }

    [Fact]
    public async Task Learning_LearnerCountsAndWatchedSeconds_FollowTheP1110ContractRules()
    {
        var courseA = Guid.NewGuid();
        var courseB = Guid.NewGuid();
        var otherCourse = Guid.NewGuid();
        var now = DateTime.UtcNow;

        var both = await CreateActorAsync(ROLE.LearnerName); // enrolled in A and B: one learner, not two
        var expired = await CreateActorAsync(ROLE.LearnerName); // Expired still counts
        var revoked = await CreateActorAsync(ROLE.LearnerName); // Revoked does not
        var recent = await CreateActorAsync(ROLE.LearnerName);
        var elsewhere = await CreateActorAsync(ROLE.LearnerName); // a course that is not asked about

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            ENROLLMENT Make(Actor learner, Guid courseId, Action<ENROLLMENT>? shape = null, DateTime? enrolledAt = null)
            {
                var enrollment = ENROLLMENT.Create(learner.UserId, courseId, null, EnrollmentSource.Purchase, null, clock);
                shape?.Invoke(enrollment);
                db.Enrollments().Add(enrollment);
                if (enrolledAt is { } at)
                {
                    db.Entry(enrollment).Property(e => e.ENROLLED_AT_UTC).CurrentValue = at;
                }

                return enrollment;
            }

            var oldDate = now.AddDays(-30);
            var bothA = Make(both, courseA, enrolledAt: oldDate);
            Make(both, courseB, enrolledAt: oldDate);
            Make(expired, courseA, e => e.Expire(), enrolledAt: oldDate);
            Make(revoked, courseA, e => e.Revoke(), enrolledAt: oldDate);
            var recentA = Make(recent, courseA);
            var elsewhereEnrollment = Make(elsewhere, otherCourse);
            await db.SaveChangesAsync();

            // Progress rows: 100 s (old), 200 s (recent), 40 s (recent, another course that must not be counted).
            var oldRow = EPISODE_PROGRESS.Create(bothA.ENROLLMENT_ID, Guid.NewGuid(), 100, 100, false, clock);
            var newRow = EPISODE_PROGRESS.Create(recentA.ENROLLMENT_ID, Guid.NewGuid(), 200, 200, false, clock);
            var elsewhereRow = EPISODE_PROGRESS.Create(elsewhereEnrollment.ENROLLMENT_ID, Guid.NewGuid(), 40, 40, false, clock);
            db.EpisodeProgresses().AddRange(oldRow, newRow, elsewhereRow);
            db.Entry(oldRow).Property(p => p.UPDATED_AT_UTC).CurrentValue = oldDate;
            await db.SaveChangesAsync();
        }

        await using var read = _factory.Services.CreateAsyncScope();
        var analytics = read.ServiceProvider.GetRequiredService<ILearningAnalyticsContract>();
        var since = now.AddDays(-1);

        var counts = await analytics.GetLearnerCountsAsync([courseA, courseB], since, CancellationToken.None);
        var seconds = await analytics.GetWatchedSecondsAsync([courseA, courseB], since, CancellationToken.None);
        var allTime = await analytics.GetWatchedSecondsAsync([courseA, courseB], now.AddDays(-365), CancellationToken.None);

        Assert.Equal(3, counts.DistinctLearners); // both (once, despite two courses), expired, recent — the revoked one is not a learner
        Assert.Equal(1, counts.DistinctLearnersSince); // only "recent" enrolled in the last day
        Assert.Equal(200L, seconds); // the old row did not move inside the window
        Assert.Equal(300L, allTime); // 100 + 200; the other course's 40 is never included
        Assert.Equal(new LearnerCounts(0, 0), await analytics.GetLearnerCountsAsync([], since, CancellationToken.None));
    }

    // ---- Commerce: the paymentId the refund request needs --------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Orders_DetailAndList_ReportThePaymentIdOfTheSuccessfulPayment_AndNullWithoutOne()
    {
        var buyer = await CreateActorAsync(ROLE.LearnerName);
        var stranger = await CreateActorAsync(ROLE.LearnerName);
        Guid paidOrderId;
        Guid unpaidOrderId;
        Guid successfulPaymentId;

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = scope.ServiceProvider.GetRequiredService<IClock>();

            var paid = Siri.Modules.Commerce.Domain.ORDER.Create($"ORD-{Guid.NewGuid():N}"[..20], buyer.UserId, 1000m, 0m, 65.42m, 1000m);
            paid.AddItem(Guid.NewGuid(), "COURSE", 1000m, 1000m);
            paid.MarkAwaitingPayment();

            // An earlier attempt that failed and the attempt that succeeded: only the successful one may be reported.
            var failedAttempt = Siri.Modules.Commerce.Domain.PAYMENT.Create(
                paid.ORDER_ID, Siri.Modules.Commerce.Domain.PaymentMethod.PromptPay, $"pi_{Guid.NewGuid():N}", 1000m, clock);
            failedAttempt.MarkFailed("expired_qr");
            var successful = Siri.Modules.Commerce.Domain.PAYMENT.Create(
                paid.ORDER_ID, Siri.Modules.Commerce.Domain.PaymentMethod.PromptPay, $"pi_{Guid.NewGuid():N}", 1000m, clock);
            successful.MarkSucceeded(clock);
            paid.MarkPaid(clock);

            var unpaid = Siri.Modules.Commerce.Domain.ORDER.Create($"ORD-{Guid.NewGuid():N}"[..20], buyer.UserId, 500m, 0m, 32.71m, 500m);
            unpaid.AddItem(Guid.NewGuid(), "COURSE 2", 500m, 500m);

            db.Set<Siri.Modules.Commerce.Domain.ORDER>().AddRange(paid, unpaid);
            db.Set<Siri.Modules.Commerce.Domain.PAYMENT>().AddRange(failedAttempt, successful);
            await db.SaveChangesAsync();

            paidOrderId = paid.ORDER_ID;
            unpaidOrderId = unpaid.ORDER_ID;
            successfulPaymentId = successful.PAYMENT_ID;
        }

        var detail = await SendAsync(HttpMethod.Get, $"/api/commerce/orders/{paidOrderId}", buyer);
        var unpaidDetail = await SendAsync(HttpMethod.Get, $"/api/commerce/orders/{unpaidOrderId}", buyer);
        var list = await SendAsync(HttpMethod.Get, "/api/commerce/orders", buyer);
        var someoneElses = await SendAsync(HttpMethod.Get, $"/api/commerce/orders/{paidOrderId}", stranger);

        Assert.Equal(HttpStatusCode.OK, detail.Status);
        using (var json = detail.Json)
        {
            Assert.Equal(successfulPaymentId, json.RootElement.GetProperty("paymentId").GetGuid());
        }

        using (var json = unpaidDetail.Json)
        {
            Assert.Equal(JsonValueKind.Null, json.RootElement.GetProperty("paymentId").ValueKind);
        }

        using (var json = list.Json)
        {
            var items = json.RootElement.GetProperty("items").EnumerateArray().ToArray();
            Assert.Equal(successfulPaymentId, items.Single(i => i.GetProperty("id").GetGuid() == paidOrderId).GetProperty("paymentId").GetGuid());
            Assert.Equal(JsonValueKind.Null, items.Single(i => i.GetProperty("id").GetGuid() == unpaidOrderId).GetProperty("paymentId").ValueKind);
        }

        // Ownership is unchanged: another user's order is a 404 and reveals no payment id.
        Assert.Equal(HttpStatusCode.NotFound, someoneElses.Status);
        Assert.DoesNotContain(successfulPaymentId.ToString(), someoneElses.Body);
    }

    // ---- Test doubles ----------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Stands in for a database that refuses the join-log commit.</summary>
    private sealed class FailingJoinLogRepository : ISessionJoinLogRepository
    {
        public void Add(SESSION_JOIN_LOG log)
        {
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) =>
            throw new InvalidOperationException("simulated outage: the join log could not be committed");

        public Task<IReadOnlyList<Guid>> GetJoinedLearnerIdsAsync(Guid sessionId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>([]);

        public Task<IReadOnlyDictionary<Guid, LearnerJoinSummary>> GetLearnerJoinSummariesAsync(
            Guid sessionId, IReadOnlyCollection<Guid> userIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, LearnerJoinSummary>>(new Dictionary<Guid, LearnerJoinSummary>());
    }

    /// <summary>Records every formatted log message (and exception text), so a test can assert that a secret was never logged.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        private readonly object _gate = new();
        private readonly List<string> _messages = [];

        public IReadOnlyList<string> Messages
        {
            get
            {
                lock (_gate)
                {
                    return _messages.ToArray();
                }
            }
        }

        public IDisposable? BeginScope<TState>(TState state)
            where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            lock (_gate)
            {
                _messages.Add(formatter(state, exception));
                if (exception is not null)
                {
                    _messages.Add(exception.ToString());
                }
            }
        }
    }
}
