using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.IntegrationTests.TestData;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.AttachSessionRecording;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Learning.Domain;
using Siri.Modules.Learning.Infrastructure;
using Siri.Modules.Media.Domain;
using Siri.Modules.Media.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// P11-06 end to end over real HTTP against the production composition root (<see cref="SiriApiFactory"/>: real PostgreSQL + Redis from Testcontainers):
/// <c>POST /api/catalog/instructor/courses/{courseId}/live-sessions/{sessionId}/recording</c> turns an uploaded recording into an ordinary lesson, which is the whole
/// catch-up feature (docs/contracts/P11-06-live-recording-catchup.md section 5). What this class proves:
/// <list type="bullet">
/// <item><b>catch-up is plain episode entitlement</b> — a learner who enrolled <em>after</em> the class ended gets a playback URL for the recording lesson through the
/// real playback endpoint; someone who is not enrolled (or whose enrollment expired, or an anonymous caller) does not — with no entitlement code of its own;</item>
/// <item>a <b>Published</b> course accepts the recording, and the public course page shows it immediately (the output cache is evicted — black box: read, attach, read);</item>
/// <item>the ownership matrix: another instructor / a learner / an admin / anonymous cannot attach, a foreign or unfinished asset is refused, ids from another course
/// are 404, and nothing changes on any refusal;</item>
/// <item>replace-in-place, idempotent repeat, the shared "บันทึกการสอนสด" section, linking an existing lesson, and the stable <c>reason</c> codes.</item>
/// </list>
/// Access tokens are minted with the real <see cref="IAccessTokenGenerator"/> (the "auth" limiter allows only five logins a minute and these tests need many users).
/// Sessions are created through the real endpoint (future, as the domain demands) and then moved into the past directly in the database.
/// <para>
/// Requires Docker like every test in this collection — without it these end in <c>DockerUnavailableException</c> (not an assertion).
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class LiveRecordingCatchUpIntegrationTests : IAsyncLifetime
{
    private const string CdnHost = "video.siriupskill.test";

    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;
    private HttpClient _client = null!;

    public LiveRecordingCatchUpIntegrationTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        // Signing a playback URL is a local HMAC (no network), so only these two playback settings are needed for the catch-up proof.
        _factory = new SiriApiFactory(_containers, new Dictionary<string, string?>
        {
            ["VideoProvider:CdnHostname"] = CdnHost,
            ["VideoProvider:TokenAuthenticationKey"] = "recording-catchup-token-auth-key-0123456789",
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();

        _client = _factory.CreateClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _factory.DisposeAsync();
    }

    // ---- Arrange helpers ------------------------------------------------------------------------------------

    private sealed record Actor(Guid UserId, string Token);

    private sealed record Scene(Actor Instructor, Guid ProfileId, Guid CourseId, string Slug, Guid SessionId);

    private sealed record Reply(HttpStatusCode Status, string Body)
    {
        public JsonDocument Json => JsonDocument.Parse(string.IsNullOrWhiteSpace(Body) ? "{}" : Body);

        public string? Reason
        {
            get
            {
                using var json = Json;
                return json.RootElement.TryGetProperty("reason", out var reason) ? reason.GetString() : null;
            }
        }
    }

    private async Task<Actor> CreateActorAsync(string roleName)
    {
        var builder = new TestUserBuilder().WithRole(roleName);

        await using var scope = _factory.Services.CreateAsyncScope();
        var user = await builder.BuildAsync(scope.ServiceProvider);
        var (token, _) = scope.ServiceProvider.GetRequiredService<IAccessTokenGenerator>().Generate(user, Guid.NewGuid());

        return new Actor(user.Id, token);
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

        var (token, _) = scope.ServiceProvider.GetRequiredService<IAccessTokenGenerator>().Generate(user, Guid.NewGuid());
        return (new Actor(user.Id, token), profile.Id);
    }

    /// <summary>An instructor, a Live course and one session created through the real endpoint, then moved to start five hours ago (it lasts two) unless <paramref name="started"/> is false.</summary>
    private async Task<Scene> CreateSceneAsync(bool started = true)
    {
        var (instructor, profileId) = await CreateInstructorAsync();
        var (courseId, slug) = await LiveIntegrationSupport.CreateLiveCourseAsync(_factory, profileId);
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, instructor.Token, courseId, daysAhead: 2);

        if (started)
        {
            await MoveSessionAsync(sessionId, DateTime.UtcNow.AddHours(-5));
        }

        return new Scene(instructor, profileId, courseId, slug, sessionId);
    }

    /// <summary>A further session on the same course (created through the endpoint, then moved to the past) — never overlapping the first.</summary>
    private async Task<Guid> AddFinishedSessionAsync(Scene scene, TimeSpan startedAgo)
    {
        var sessionId = await LiveIntegrationSupport.CreateSessionAsync(_client, scene.Instructor.Token, scene.CourseId, daysAhead: 4);
        await MoveSessionAsync(sessionId, DateTime.UtcNow - startedAgo);
        return sessionId;
    }

    /// <summary>Rewrites a session's window directly — the domain refuses to schedule into the past.</summary>
    private async Task MoveSessionAsync(Guid sessionId, DateTime startsAtUtc)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var session = await db.CourseLiveSessions().SingleAsync(s => s.Id == sessionId);
        db.Entry(session).Property(s => s.StartsAtUtc).CurrentValue = startsAtUtc;
        db.Entry(session).Property(s => s.EndsAtUtc).CurrentValue = startsAtUtc.AddHours(2);
        await db.SaveChangesAsync();
    }

    private async Task<Guid> CreateAssetAsync(Guid uploadedByUserId, string status = "Ready", int durationSeconds = 600)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var asset = MEDIA_ASSET.Create("BunnyStream", $"bunny-{Guid.NewGuid():N}", uploadedByUserId, drmEnabled: false);
        switch (status)
        {
            case "Ready":
                asset.MarkReady("playback-id", durationSeconds, null, clock);
                break;
            case "Processing":
                asset.MarkProcessing();
                break;
        }

        db.MediaAssets().Add(asset);
        await db.SaveChangesAsync();
        return asset.MEDIA_ASSET_ID;
    }

    /// <summary>A lesson of the course (in a fresh section) that already has media — the target of mode B.</summary>
    private async Task<Guid> AddLessonWithMediaAsync(Guid courseId, bool withMedia = true)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var course = await db.Courses().Include(c => c.Sections).ThenInclude(s => s.Episodes).SingleAsync(c => c.Id == courseId);
        var section = course.AddSection("บทที่ 1");
        db.Entry(section).State = EntityState.Added;
        var episode = course.AddEpisode(section.Id, "บทเรียนที่มีอยู่แล้ว", null, false);
        db.Entry(episode).State = EntityState.Added;
        if (withMedia)
        {
            course.AttachEpisodeMedia(episode.Id, Guid.NewGuid(), 420);
        }

        await db.SaveChangesAsync();
        return episode.Id;
    }

    private async Task SetCourseStatusAsync(Guid courseId, Action<COURSE, IClock> change)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var course = await db.Courses().Include(c => c.LiveSessions).SingleAsync(c => c.Id == courseId);
        change(course, clock);
        await db.SaveChangesAsync();
    }

    private async Task EnrollAsync(Guid userId, Guid courseId, DateTime? expiresAtUtc = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        db.Enrollments().Add(ENROLLMENT.Create(userId, courseId, null, EnrollmentSource.Purchase, expiresAtUtc, clock));
        await db.SaveChangesAsync();
    }

    private async Task<Reply> SendAsync(HttpMethod method, string uri, Actor? actor, object? json = null)
    {
        using var request = new HttpRequestMessage(method, uri);
        if (actor is not null)
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", actor.Token);
        }

        if (json is not null)
        {
            request.Content = JsonContent.Create(json);
        }

        using var response = await _client.SendAsync(request);
        return new Reply(response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    private Task<Reply> AttachAsync(Actor? actor, Guid courseId, Guid sessionId, object body) =>
        SendAsync(HttpMethod.Post, $"/api/catalog/instructor/courses/{courseId}/live-sessions/{sessionId}/recording", actor, body);

    private async Task<COURSE> LoadCourseAsync(Guid courseId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        return await db.Courses().AsNoTracking()
            .Include(c => c.Sections).ThenInclude(s => s.Episodes)
            .Include(c => c.LiveSessions)
            .AsSplitQuery()
            .SingleAsync(c => c.Id == courseId);
    }

    private static void AssertNothingAttached(COURSE course, Guid sessionId)
    {
        Assert.Empty(course.Sections);
        Assert.Equal(0, course.EpisodeCount);
        Assert.Null(course.LiveSessions.Single(s => s.Id == sessionId).RecordingEpisodeId);
    }

    // ---- Happy path + contract shape ---------------------------------------------------------------------------

    [Fact]
    public async Task Attach_FirstRecording_Returns200WithTheContractShape_AndPersistsTheSectionLessonAndLink()
    {
        var scene = await CreateSceneAsync();
        var assetId = await CreateAssetAsync(scene.Instructor.UserId, durationSeconds: 3_725);

        var reply = await AttachAsync(scene.Instructor, scene.CourseId, scene.SessionId, new { mediaAssetId = assetId, episodeTitle = "บันทึก: Kickoff" });

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        using var json = reply.Json;
        var root = json.RootElement;
        Assert.Equal(
            ["durationSeconds", "episodeTitle", "mediaAssetId", "recordingEpisodeId", "replacedExisting", "sectionId", "sessionId"],
            root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(scene.SessionId, root.GetProperty("sessionId").GetGuid());
        Assert.Equal("บันทึก: Kickoff", root.GetProperty("episodeTitle").GetString());
        Assert.Equal(3_725, root.GetProperty("durationSeconds").GetInt32());
        Assert.Equal(assetId, root.GetProperty("mediaAssetId").GetGuid());
        Assert.False(root.GetProperty("replacedExisting").GetBoolean());

        var course = await LoadCourseAsync(scene.CourseId);
        var section = Assert.Single(course.Sections);
        Assert.Equal("บันทึกการสอนสด", section.Title);
        Assert.Equal(section.Id, root.GetProperty("sectionId").GetGuid());
        var episode = Assert.Single(section.Episodes);
        Assert.Equal(episode.Id, root.GetProperty("recordingEpisodeId").GetGuid());
        Assert.Equal(assetId, episode.MediaAssetId);
        Assert.Equal(3_725, episode.DurationSeconds);
        Assert.Equal(CourseEpisodeStatus.Ready, episode.Status);
        Assert.False(episode.IsFreePreview);
        Assert.Equal(episode.Id, course.LiveSessions.Single().RecordingEpisodeId);
        Assert.Equal(1, course.EpisodeCount);
        Assert.Equal(3_725, course.TotalDurationSeconds);

        // The instructor's schedule read (appendix C.1) now carries the link the front end uses to refetch.
        var schedule = await SendAsync(HttpMethod.Get, $"/api/catalog/instructor/courses/{scene.CourseId}/live-schedule", scene.Instructor);
        Assert.Equal(HttpStatusCode.OK, schedule.Status);
        using var scheduleJson = schedule.Json;
        Assert.Equal(episode.Id, scheduleJson.RootElement.GetProperty("sessions")[0].GetProperty("recordingEpisodeId").GetGuid());
    }

    [Fact]
    public async Task Attach_SecondSession_ReusesTheRecordingsSection_NoDuplicate()
    {
        var scene = await CreateSceneAsync();
        var secondSessionId = await AddFinishedSessionAsync(scene, startedAgo: TimeSpan.FromHours(2));
        var assetOne = await CreateAssetAsync(scene.Instructor.UserId, durationSeconds: 100);
        var assetTwo = await CreateAssetAsync(scene.Instructor.UserId, durationSeconds: 200);

        Assert.Equal(HttpStatusCode.OK, (await AttachAsync(scene.Instructor, scene.CourseId, scene.SessionId, new { mediaAssetId = assetOne })).Status);
        Assert.Equal(HttpStatusCode.OK, (await AttachAsync(scene.Instructor, scene.CourseId, secondSessionId, new { mediaAssetId = assetTwo })).Status);

        var course = await LoadCourseAsync(scene.CourseId);
        var section = Assert.Single(course.Sections);
        Assert.Equal("บันทึกการสอนสด", section.Title);
        Assert.Equal(2, section.Episodes.Count);
        Assert.Equal([0, 1], section.Episodes.Select(e => e.SortOrder).Order().ToArray());
        Assert.Equal(2, course.EpisodeCount);
        Assert.Equal(300, course.TotalDurationSeconds);
        Assert.All(course.LiveSessions, s => Assert.NotNull(s.RecordingEpisodeId));
    }

    [Fact]
    public async Task Attach_WithAnExplicitSection_PutsTheLessonThere_AndCreatesNoSection()
    {
        var scene = await CreateSceneAsync();
        await AddLessonWithMediaAsync(scene.CourseId); // creates "บทที่ 1" with one lesson
        var existing = await LoadCourseAsync(scene.CourseId);
        var sectionId = existing.Sections.Single().Id;
        var assetId = await CreateAssetAsync(scene.Instructor.UserId);

        var reply = await AttachAsync(scene.Instructor, scene.CourseId, scene.SessionId, new { mediaAssetId = assetId, sectionId });

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        var course = await LoadCourseAsync(scene.CourseId);
        var section = Assert.Single(course.Sections);
        Assert.Equal(2, section.Episodes.Count);
        using var json = reply.Json;
        Assert.Equal(sectionId, json.RootElement.GetProperty("sectionId").GetGuid());
    }

    [Fact]
    public async Task Attach_ToAPublishedCourse_Succeeds_AndThePublicPageShowsTheRecordingImmediately()
    {
        var scene = await CreateSceneAsync(started: false);

        // Publishable as a Live course with a future scheduled session; then the class "happens".
        await SetCourseStatusAsync(scene.CourseId, (course, clock) => course.Publish(clock));

        // Black-box proof that the output cache is evicted: read the public page (populating its cache), attach, read again.
        var before = await SendAsync(HttpMethod.Get, $"/api/catalog/courses/{scene.Slug}", actor: null);
        Assert.Equal(HttpStatusCode.OK, before.Status);
        using (var beforeJson = before.Json)
        {
            Assert.Equal(0, beforeJson.RootElement.GetProperty("episodeCount").GetInt32());
            Assert.False(beforeJson.RootElement.GetProperty("liveSchedule").GetProperty("sessions")[0].GetProperty("hasRecording").GetBoolean());
        }

        await MoveSessionAsync(scene.SessionId, DateTime.UtcNow.AddHours(-5));
        var assetId = await CreateAssetAsync(scene.Instructor.UserId, durationSeconds: 1_800);
        var attach = await AttachAsync(scene.Instructor, scene.CourseId, scene.SessionId, new { mediaAssetId = assetId });
        Assert.Equal(HttpStatusCode.OK, attach.Status);

        var after = await SendAsync(HttpMethod.Get, $"/api/catalog/courses/{scene.Slug}", actor: null);
        Assert.Equal(HttpStatusCode.OK, after.Status);
        using var afterJson = after.Json;
        Assert.Equal(1, afterJson.RootElement.GetProperty("episodeCount").GetInt32());
        Assert.Equal(1_800, afterJson.RootElement.GetProperty("totalDurationSeconds").GetInt32());
        Assert.True(afterJson.RootElement.GetProperty("liveSchedule").GetProperty("sessions")[0].GetProperty("hasRecording").GetBoolean());
        Assert.Equal("Live", afterJson.RootElement.GetProperty("deliveryFormat").GetString());

        var course = await LoadCourseAsync(scene.CourseId);
        Assert.Equal(CourseStatus.Published, course.Status);
    }

    // ---- Catch-up = plain episode entitlement (security-critical) -------------------------------------------------------

    [Fact]
    public async Task Playback_ForTheRecordingLesson_LatecomerEnrolledAfterTheClass_GetsAUrl_OthersDoNot()
    {
        var scene = await CreateSceneAsync(); // started five hours ago, ended three hours ago
        var assetId = await CreateAssetAsync(scene.Instructor.UserId);
        var attach = await AttachAsync(scene.Instructor, scene.CourseId, scene.SessionId, new { mediaAssetId = assetId });
        Assert.Equal(HttpStatusCode.OK, attach.Status);
        Guid recordingEpisodeId;
        using (var attachJson = attach.Json)
        {
            recordingEpisodeId = attachJson.RootElement.GetProperty("recordingEpisodeId").GetGuid();
        }

        // The latecomer buys (is enrolled) only now — strictly after the class ended. There is no per-session entitlement to grant: it is the course's lesson.
        var latecomer = await CreateActorAsync(ROLE.LearnerName);
        await EnrollAsync(latecomer.UserId, scene.CourseId);
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var enrolledAt = await db.Enrollments().AsNoTracking().Where(e => e.USER_ID == latecomer.UserId && e.COURSE_ID == scene.CourseId).Select(e => e.ENROLLED_AT_UTC).SingleAsync();
            var sessionEnd = await db.CourseLiveSessions().AsNoTracking().Where(s => s.Id == scene.SessionId).Select(s => s.EndsAtUtc).SingleAsync();
            Assert.True(enrolledAt > sessionEnd, "the learner must have enrolled after the session ended");
        }

        var playback = await SendAsync(HttpMethod.Get, $"/api/media/playback/{recordingEpisodeId}?deviceId=catchup-device", latecomer);

        Assert.Equal(HttpStatusCode.OK, playback.Status);
        using (var playbackJson = playback.Json)
        {
            var manifest = playbackJson.RootElement.GetProperty("manifestUrl").GetString();
            Assert.Contains(CdnHost, manifest);
            Assert.False(string.IsNullOrWhiteSpace(playbackJson.RootElement.GetProperty("watermarkPayload").GetString()));
        }

        // A learner enrolled earlier is no different.
        var early = await CreateActorAsync(ROLE.LearnerName);
        await EnrollAsync(early.UserId, scene.CourseId);
        Assert.Equal(HttpStatusCode.OK, (await SendAsync(HttpMethod.Get, $"/api/media/playback/{recordingEpisodeId}?deviceId=d2", early)).Status);

        // Not enrolled, expired enrollment, anonymous: refused (the recording lesson is a paid lesson like any other).
        var outsider = await CreateActorAsync(ROLE.LearnerName);
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, $"/api/media/playback/{recordingEpisodeId}?deviceId=d3", outsider)).Status);

        var lapsed = await CreateActorAsync(ROLE.LearnerName);
        await EnrollAsync(lapsed.UserId, scene.CourseId, expiresAtUtc: DateTime.UtcNow.AddMinutes(-1));
        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, $"/api/media/playback/{recordingEpisodeId}?deviceId=d4", lapsed)).Status);

        Assert.Equal(HttpStatusCode.Forbidden, (await SendAsync(HttpMethod.Get, $"/api/media/playback/{recordingEpisodeId}", actor: null)).Status);
    }

    // ---- Replace in place / idempotent repeat -------------------------------------------------------------------------------

    [Fact]
    public async Task Attach_AnotherAssetToTheSameSession_ReplacesTheMediaInPlace_AndTheSameAssetAgainIsIdempotent()
    {
        var scene = await CreateSceneAsync();
        var assetA = await CreateAssetAsync(scene.Instructor.UserId, durationSeconds: 600);
        var assetB = await CreateAssetAsync(scene.Instructor.UserId, durationSeconds: 900);

        var first = await AttachAsync(scene.Instructor, scene.CourseId, scene.SessionId, new { mediaAssetId = assetA, episodeTitle = "ฉบับแรก" });
        Assert.Equal(HttpStatusCode.OK, first.Status);
        Guid episodeId;
        using (var firstJson = first.Json)
        {
            episodeId = firstJson.RootElement.GetProperty("recordingEpisodeId").GetGuid();
        }

        var replaced = await AttachAsync(scene.Instructor, scene.CourseId, scene.SessionId, new { mediaAssetId = assetB });

        Assert.Equal(HttpStatusCode.OK, replaced.Status);
        using (var replacedJson = replaced.Json)
        {
            Assert.True(replacedJson.RootElement.GetProperty("replacedExisting").GetBoolean());
            Assert.Equal(episodeId, replacedJson.RootElement.GetProperty("recordingEpisodeId").GetGuid());
            Assert.Equal(assetB, replacedJson.RootElement.GetProperty("mediaAssetId").GetGuid());
            Assert.Equal(900, replacedJson.RootElement.GetProperty("durationSeconds").GetInt32());
        }

        var course = await LoadCourseAsync(scene.CourseId);
        var episode = Assert.Single(course.Sections.SelectMany(s => s.Episodes));
        Assert.Equal(episodeId, episode.Id);
        Assert.Equal(assetB, episode.MediaAssetId);
        Assert.Equal("ฉบับแรก", episode.Title); // no title supplied on the replace: untouched
        Assert.Equal(900, course.TotalDurationSeconds);

        var again = await AttachAsync(scene.Instructor, scene.CourseId, scene.SessionId, new { mediaAssetId = assetB });
        Assert.Equal(HttpStatusCode.OK, again.Status);
        using var againJson = again.Json;
        Assert.False(againJson.RootElement.GetProperty("replacedExisting").GetBoolean());
        Assert.Equal(episodeId, againJson.RootElement.GetProperty("recordingEpisodeId").GetGuid());
        Assert.Equal(1, (await LoadCourseAsync(scene.CourseId)).EpisodeCount);
    }

    // ---- Mode B: link an existing lesson ------------------------------------------------------------------------------------

    [Fact]
    public async Task Attach_ExistingLesson_LinksIt_AndRefusesALessonWithoutMediaOrAlreadyUsedByAnotherSession()
    {
        var scene = await CreateSceneAsync();
        var secondSessionId = await AddFinishedSessionAsync(scene, startedAgo: TimeSpan.FromHours(2));
        var lessonId = await AddLessonWithMediaAsync(scene.CourseId);
        var bareLessonId = await AddLessonWithMediaAsync(scene.CourseId, withMedia: false);

        var linked = await AttachAsync(scene.Instructor, scene.CourseId, scene.SessionId, new { episodeId = lessonId });

        Assert.Equal(HttpStatusCode.OK, linked.Status);
        using (var linkedJson = linked.Json)
        {
            Assert.Equal(lessonId, linkedJson.RootElement.GetProperty("recordingEpisodeId").GetGuid());
            Assert.Equal("บทเรียนที่มีอยู่แล้ว", linkedJson.RootElement.GetProperty("episodeTitle").GetString());
            Assert.False(linkedJson.RootElement.GetProperty("replacedExisting").GetBoolean());
        }

        Assert.Equal(lessonId, (await LoadCourseAsync(scene.CourseId)).LiveSessions.Single(s => s.Id == scene.SessionId).RecordingEpisodeId);

        var noMedia = await AttachAsync(scene.Instructor, scene.CourseId, secondSessionId, new { episodeId = bareLessonId });
        Assert.Equal(HttpStatusCode.Conflict, noMedia.Status);
        Assert.Equal("live.recording_episode_has_no_media", noMedia.Reason);

        var inUse = await AttachAsync(scene.Instructor, scene.CourseId, secondSessionId, new { episodeId = lessonId });
        Assert.Equal(HttpStatusCode.Conflict, inUse.Status);
        Assert.Equal("live.recording_episode_in_use", inUse.Reason);

        var unknown = await AttachAsync(scene.Instructor, scene.CourseId, secondSessionId, new { episodeId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, unknown.Status);

        Assert.Null((await LoadCourseAsync(scene.CourseId)).LiveSessions.Single(s => s.Id == secondSessionId).RecordingEpisodeId);
    }

    // ---- Refusals: state ------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Attach_SessionNotStartedYet_Is409_WithTheStableReason_AndNothingChanges()
    {
        var scene = await CreateSceneAsync(started: false);
        var assetId = await CreateAssetAsync(scene.Instructor.UserId);

        var reply = await AttachAsync(scene.Instructor, scene.CourseId, scene.SessionId, new { mediaAssetId = assetId });

        Assert.Equal(HttpStatusCode.Conflict, reply.Status);
        Assert.Equal("live.session_not_started", reply.Reason);
        AssertNothingAttached(await LoadCourseAsync(scene.CourseId), scene.SessionId);
    }

    [Fact]
    public async Task Attach_CancelledSession_IsStillAllowed()
    {
        var scene = await CreateSceneAsync(started: false);
        var cancel = await SendAsync(
            HttpMethod.Post, $"/api/catalog/instructor/courses/{scene.CourseId}/live-sessions/{scene.SessionId}/cancel", scene.Instructor, new { reason = "ผู้สอนไม่สะดวก" });
        Assert.Equal(HttpStatusCode.OK, cancel.Status);
        await MoveSessionAsync(scene.SessionId, DateTime.UtcNow.AddHours(-5));
        var assetId = await CreateAssetAsync(scene.Instructor.UserId);

        var reply = await AttachAsync(scene.Instructor, scene.CourseId, scene.SessionId, new { mediaAssetId = assetId });

        Assert.Equal(HttpStatusCode.OK, reply.Status);
        var session = (await LoadCourseAsync(scene.CourseId)).LiveSessions.Single();
        Assert.Equal(CourseLiveSessionStatus.Cancelled, session.Status);
        Assert.NotNull(session.RecordingEpisodeId);
    }

    [Fact]
    public async Task Attach_ArchivedCourse_Is409_AndNothingChanges()
    {
        var scene = await CreateSceneAsync();
        await SetCourseStatusAsync(scene.CourseId, (course, _) => course.Archive());
        var assetId = await CreateAssetAsync(scene.Instructor.UserId);

        var reply = await AttachAsync(scene.Instructor, scene.CourseId, scene.SessionId, new { mediaAssetId = assetId });

        Assert.Equal(HttpStatusCode.Conflict, reply.Status);
        AssertNothingAttached(await LoadCourseAsync(scene.CourseId), scene.SessionId);
    }

    // ---- Refusals: the asset ---------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Attach_AssetOfAnotherInstructor_Is403_UnknownAssetIs404_NotReadyIs409_AndNothingChanges()
    {
        var scene = await CreateSceneAsync();
        var (other, _) = await CreateInstructorAsync();
        var foreignAsset = await CreateAssetAsync(other.UserId);
        var processingAsset = await CreateAssetAsync(scene.Instructor.UserId, status: "Processing");
        var uploadingAsset = await CreateAssetAsync(scene.Instructor.UserId, status: "Uploading");

        var foreign = await AttachAsync(scene.Instructor, scene.CourseId, scene.SessionId, new { mediaAssetId = foreignAsset });
        Assert.Equal(HttpStatusCode.Forbidden, foreign.Status);

        var unknown = await AttachAsync(scene.Instructor, scene.CourseId, scene.SessionId, new { mediaAssetId = Guid.NewGuid() });
        Assert.Equal(HttpStatusCode.NotFound, unknown.Status);

        foreach (var notReady in new[] { processingAsset, uploadingAsset })
        {
            var reply = await AttachAsync(scene.Instructor, scene.CourseId, scene.SessionId, new { mediaAssetId = notReady });
            Assert.Equal(HttpStatusCode.Conflict, reply.Status);
            Assert.Equal("live.recording_asset_not_ready", reply.Reason);
        }

        AssertNothingAttached(await LoadCourseAsync(scene.CourseId), scene.SessionId);
    }

    [Fact]
    public async Task Attach_TheSameAssetToASecondSession_Is409_AssetInUse()
    {
        var scene = await CreateSceneAsync();
        var secondSessionId = await AddFinishedSessionAsync(scene, startedAgo: TimeSpan.FromHours(2));
        var assetId = await CreateAssetAsync(scene.Instructor.UserId);
        Assert.Equal(HttpStatusCode.OK, (await AttachAsync(scene.Instructor, scene.CourseId, scene.SessionId, new { mediaAssetId = assetId })).Status);

        var reply = await AttachAsync(scene.Instructor, scene.CourseId, secondSessionId, new { mediaAssetId = assetId });

        Assert.Equal(HttpStatusCode.Conflict, reply.Status);
        Assert.Equal("live.recording_asset_in_use", reply.Reason);
        var course = await LoadCourseAsync(scene.CourseId);
        Assert.Null(course.LiveSessions.Single(s => s.Id == secondSessionId).RecordingEpisodeId);
        Assert.Equal(1, course.EpisodeCount);
    }

    // ---- Refusals: authorization / IDOR matrix --------------------------------------------------------------------------------------

    [Fact]
    public async Task Attach_IdorMatrix_NoOneButTheOwningInstructorCanAttach_AndNothingEverChanges()
    {
        var scene = await CreateSceneAsync();
        var assetId = await CreateAssetAsync(scene.Instructor.UserId);
        var (otherInstructor, _) = await CreateInstructorAsync();
        var otherAsset = await CreateAssetAsync(otherInstructor.UserId);
        var learner = await CreateActorAsync(ROLE.LearnerName);
        var admin = await CreateActorAsync(ROLE.AdminName);
        var body = new { mediaAssetId = assetId };

        // Another approved instructor — even with their own perfectly valid asset — cannot touch this course.
        Assert.Equal(HttpStatusCode.Forbidden, (await AttachAsync(otherInstructor, scene.CourseId, scene.SessionId, new { mediaAssetId = otherAsset })).Status);
        // A learner never reaches the handler (InstructorOnly), an administrator is not an owner, an anonymous caller is not signed in.
        Assert.Equal(HttpStatusCode.Forbidden, (await AttachAsync(learner, scene.CourseId, scene.SessionId, body)).Status);
        Assert.Equal(HttpStatusCode.Forbidden, (await AttachAsync(admin, scene.CourseId, scene.SessionId, body)).Status);
        Assert.Equal(HttpStatusCode.Unauthorized, (await AttachAsync(actor: null, scene.CourseId, scene.SessionId, body)).Status);

        // Ids that do not line up.
        Assert.Equal(HttpStatusCode.NotFound, (await AttachAsync(scene.Instructor, Guid.NewGuid(), scene.SessionId, body)).Status);
        Assert.Equal(HttpStatusCode.NotFound, (await AttachAsync(scene.Instructor, scene.CourseId, Guid.NewGuid(), body)).Status);
        var otherScene = await CreateSceneAsync();
        Assert.Equal(HttpStatusCode.NotFound, (await AttachAsync(scene.Instructor, scene.CourseId, otherScene.SessionId, body)).Status); // a real session, but of another course
        Assert.Equal(HttpStatusCode.NotFound, (await AttachAsync(scene.Instructor, scene.CourseId, scene.SessionId, new { mediaAssetId = assetId, sectionId = Guid.NewGuid() })).Status);

        AssertNothingAttached(await LoadCourseAsync(scene.CourseId), scene.SessionId);
        AssertNothingAttached(await LoadCourseAsync(otherScene.CourseId), otherScene.SessionId);
    }

    [Fact]
    public async Task Attach_InvalidBodies_Are400_AndNothingChanges()
    {
        var scene = await CreateSceneAsync();
        var assetId = await CreateAssetAsync(scene.Instructor.UserId);
        var lessonId = Guid.NewGuid();

        object[] bodies =
        [
            new { },                                                                  // neither target
            new { mediaAssetId = assetId, episodeId = lessonId },                     // both targets
            new { episodeId = lessonId, sectionId = Guid.NewGuid() },                 // a section with a lesson target
            new { mediaAssetId = Guid.Empty },                                        // empty id
            new { mediaAssetId = assetId, episodeTitle = new string('ก', 201) },      // title too long
        ];

        foreach (var body in bodies)
        {
            var reply = await AttachAsync(scene.Instructor, scene.CourseId, scene.SessionId, body);
            Assert.Equal(HttpStatusCode.BadRequest, reply.Status);
        }

        AssertNothingAttached(await LoadCourseAsync(scene.CourseId), scene.SessionId);
    }

    // ---- Rate limit -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Attach_IsOnThePerUserLiveUserLimiter_61stRequestInAMinuteIs429_AnotherInstructorIsUnaffected()
    {
        var scene = await CreateSceneAsync();
        var (otherInstructor, _) = await CreateInstructorAsync();

        // Invalid bodies are enough: the limiter runs before the action, so every request counts (and none of them changes anything).
        var statuses = new List<HttpStatusCode>();
        for (var i = 0; i < 61; i++)
        {
            statuses.Add((await AttachAsync(scene.Instructor, scene.CourseId, scene.SessionId, new { })).Status);
        }

        Assert.All(statuses.Take(60), status => Assert.Equal(HttpStatusCode.BadRequest, status));
        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[60]);

        // The window is per user, not app-wide like the class-level "default" policy: someone else still gets through to the handler (403, not 429).
        Assert.Equal(HttpStatusCode.Forbidden, (await AttachAsync(otherInstructor, scene.CourseId, scene.SessionId, new { mediaAssetId = Guid.NewGuid() })).Status);
    }
}
