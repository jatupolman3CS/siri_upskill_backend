using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.IntegrationTests.TestData;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Media.Domain;
using Siri.Modules.Media.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// P11-13: <see cref="ILiveRecordingAttacher"/> against real PostgreSQL through the production composition root. Proves the read side maps what the
/// recording import needs (instructor user id via the profile, the ended window, cancelled and has-recording flags, archived courses hidden) and that
/// <see cref="ILiveRecordingAttacher.AttachAsync"/> is the manual attach unchanged — same ownership, asset and timing rules, same stable reasons.
/// <para>
/// Requires Docker like every test in this collection — without it these end in <c>DockerUnavailableException</c> (not an assertion).
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class LiveRecordingAttacherIntegrationTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;

    public LiveRecordingAttacherIntegrationTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        _factory = new SiriApiFactory(_containers);

        await using var scope = _factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _factory.DisposeAsync();

    // ---- Arrange helpers ------------------------------------------------------------------------------------

    private sealed class SetupClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow => utcNow;
    }

    private sealed record Scene(Guid InstructorUserId, Guid ProfileId, Guid CourseId, Guid SessionId, DateTime StartsAtUtc, DateTime EndsAtUtc);

    /// <summary>An approved instructor, a Live course and one session that started <paramref name="startedAgo"/> ago and lasts two hours.</summary>
    private async Task<Scene> CreateSceneAsync(TimeSpan startedAgo, Action<COURSE, COURSE_LIVE_SESSION, IClock>? customize = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var user = await new TestUserBuilder().WithRole(ROLE.InstructorName).BuildAsync(scope.ServiceProvider);
        var profile = INSTRUCTOR_PROFILE.Apply(user.Id, "Test Instructor", "Headline", "Bio");
        profile.Approve(clock);
        db.InstructorProfiles().Add(profile);

        var category = CATEGORY.Create($"category-{Guid.NewGuid():N}", "หมวดหมู่ทดสอบ", "Test CATEGORY", null, null, 0);
        db.Categories().Add(category);

        var course = COURSE.Create(
            $"course-{Guid.NewGuid():N}", "คอร์สสดสำหรับทดสอบ", profile.Id, category.Id, CourseLevel.Beginner, CourseLanguage.Thai, 1200m);
        course.SetDeliveryFormat(DeliveryFormat.Live);

        // The domain refuses to schedule into the past, so the session is created "as of" ten days ago.
        // Truncated to whole milliseconds: the columns are timestamptz(3), so a finer value would not read back equal.
        var rawStartsAtUtc = DateTime.UtcNow - startedAgo;
        var startsAtUtc = new DateTime(rawStartsAtUtc.Ticks - (rawStartsAtUtc.Ticks % TimeSpan.TicksPerMillisecond), DateTimeKind.Utc);
        var session = course.AddLiveSession("คาบทดสอบ", null, startsAtUtc, startsAtUtc.AddHours(2), new SetupClock(DateTime.UtcNow.AddDays(-10)));
        customize?.Invoke(course, session, new SetupClock(DateTime.UtcNow.AddDays(-10)));

        // Added whole, so every child of the graph is an insert.
        db.Courses().Add(course);
        await db.SaveChangesAsync();

        return new Scene(user.Id, profile.Id, course.Id, session.Id, session.StartsAtUtc, session.EndsAtUtc);
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

    private async Task<TResult> WithAttacherAsync<TResult>(Func<ILiveRecordingAttacher, Task<TResult>> action)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        return await action(scope.ServiceProvider.GetRequiredService<ILiveRecordingAttacher>());
    }

    // ---- ListEndedAsync / GetAsync -------------------------------------------------------------------------------

    [Fact]
    public async Task GetAsync_ReturnsTheSessionWithTheInstructorUserIdNotTheProfileId()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromHours(5));

        var session = await WithAttacherAsync(a => a.GetAsync(scene.SessionId, CancellationToken.None));

        Assert.NotNull(session);
        Assert.Equal(scene.SessionId, session.SessionId);
        Assert.Equal(scene.CourseId, session.CourseId);
        Assert.Equal(scene.InstructorUserId, session.InstructorUserId);
        Assert.NotEqual(scene.ProfileId, session.InstructorUserId);
        Assert.Equal("คาบทดสอบ", session.Title);
        Assert.Equal(scene.StartsAtUtc, session.StartsAtUtc);
        Assert.Equal(scene.EndsAtUtc, session.EndsAtUtc);
        Assert.False(session.HasRecording);
        Assert.False(session.IsCancelled);
    }

    [Fact]
    public async Task GetAsync_UnknownSession_IsNull()
    {
        var session = await WithAttacherAsync(a => a.GetAsync(Guid.NewGuid(), CancellationToken.None));

        Assert.Null(session);
    }

    [Fact]
    public async Task ListEndedAsync_ListsOnlySessionsEndedInsideTheRange_OldestFirst()
    {
        var older = await CreateSceneAsync(TimeSpan.FromHours(30));   // ended ~28 h ago
        var recent = await CreateSceneAsync(TimeSpan.FromHours(6));   // ended ~4 h ago
        var running = await CreateSceneAsync(TimeSpan.FromHours(1));  // still running (ends in ~1 h)

        var listed = await WithAttacherAsync(a => a.ListEndedAsync(DateTime.UtcNow.AddHours(-48), DateTime.UtcNow, 100, CancellationToken.None));

        var ids = listed.Select(s => s.SessionId).ToList();
        Assert.Contains(older.SessionId, ids);
        Assert.Contains(recent.SessionId, ids);
        Assert.DoesNotContain(running.SessionId, ids);
        Assert.True(ids.IndexOf(older.SessionId) < ids.IndexOf(recent.SessionId));

        var narrow = await WithAttacherAsync(a => a.ListEndedAsync(DateTime.UtcNow.AddHours(-10), DateTime.UtcNow, 100, CancellationToken.None));
        Assert.Contains(recent.SessionId, narrow.Select(s => s.SessionId));
        Assert.DoesNotContain(older.SessionId, narrow.Select(s => s.SessionId));
    }

    [Fact]
    public async Task ListEndedAsync_HonoursTheLimit_AndAnEmptyRangeIsEmpty()
    {
        await CreateSceneAsync(TimeSpan.FromHours(6));
        await CreateSceneAsync(TimeSpan.FromHours(7));

        var limited = await WithAttacherAsync(a => a.ListEndedAsync(DateTime.UtcNow.AddHours(-48), DateTime.UtcNow, 1, CancellationToken.None));
        var empty = await WithAttacherAsync(a => a.ListEndedAsync(DateTime.UtcNow, DateTime.UtcNow.AddHours(-1), 10, CancellationToken.None));
        var none = await WithAttacherAsync(a => a.ListEndedAsync(DateTime.UtcNow.AddHours(-48), DateTime.UtcNow, 0, CancellationToken.None));

        Assert.Single(limited);
        Assert.Empty(empty);
        Assert.Empty(none);
    }

    [Fact]
    public async Task ListEndedAsync_FlagsACancelledSession()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromHours(6), (course, session, clock) => course.CancelLiveSession(session.Id, "ยกเลิก", clock));

        var listed = await WithAttacherAsync(a => a.ListEndedAsync(DateTime.UtcNow.AddHours(-48), DateTime.UtcNow, 100, CancellationToken.None));

        var item = Assert.Single(listed, s => s.SessionId == scene.SessionId);
        Assert.True(item.IsCancelled);
    }

    [Fact]
    public async Task ListEndedAsync_SessionsOfAnArchivedCourseAreNotListed_AndGetReadsThemAsAbsent()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromHours(6), (course, _, _) => course.Archive());

        var listed = await WithAttacherAsync(a => a.ListEndedAsync(DateTime.UtcNow.AddHours(-48), DateTime.UtcNow, 100, CancellationToken.None));
        var single = await WithAttacherAsync(a => a.GetAsync(scene.SessionId, CancellationToken.None));

        Assert.DoesNotContain(listed, s => s.SessionId == scene.SessionId);
        Assert.Null(single);
    }

    // ---- AttachAsync = the manual attach -------------------------------------------------------------------------

    [Fact]
    public async Task AttachAsync_ReadyAssetOfTheInstructor_BecomesTheSessionsRecordingLesson()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromHours(5));
        var assetId = await CreateAssetAsync(scene.InstructorUserId, durationSeconds: 3_725);

        var attached = await WithAttacherAsync(a => a.AttachAsync(scene.InstructorUserId, scene.CourseId, scene.SessionId, assetId, null, CancellationToken.None));

        Assert.True(attached.IsSuccess);

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = await db.Courses().AsNoTracking()
            .Include(c => c.Sections).ThenInclude(s => s.Episodes)
            .Include(c => c.LiveSessions)
            .AsSplitQuery()
            .SingleAsync(c => c.Id == scene.CourseId);
        var episode = Assert.Single(Assert.Single(course.Sections).Episodes);
        Assert.Equal(episode.Id, attached.Value.EpisodeId);
        Assert.Equal(assetId, episode.MediaAssetId);
        Assert.Equal(3_725, episode.DurationSeconds);
        Assert.Equal("บันทึก: คาบทดสอบ", episode.Title);
        Assert.Equal(episode.Id, course.LiveSessions.Single().RecordingEpisodeId);

        // The read side now reports the recording.
        var session = await WithAttacherAsync(a => a.GetAsync(scene.SessionId, CancellationToken.None));
        Assert.True(session!.HasRecording);
    }

    [Fact]
    public async Task AttachAsync_TheSameAssetAgain_IsIdempotent()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromHours(5));
        var assetId = await CreateAssetAsync(scene.InstructorUserId);

        var first = await WithAttacherAsync(a => a.AttachAsync(scene.InstructorUserId, scene.CourseId, scene.SessionId, assetId, null, CancellationToken.None));
        var second = await WithAttacherAsync(a => a.AttachAsync(scene.InstructorUserId, scene.CourseId, scene.SessionId, assetId, null, CancellationToken.None));

        Assert.True(second.IsSuccess);
        Assert.Equal(first.Value.EpisodeId, second.Value.EpisodeId);
    }

    [Fact]
    public async Task AttachAsync_ACustomTitle_NamesTheLesson()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromHours(5));
        var assetId = await CreateAssetAsync(scene.InstructorUserId);

        await WithAttacherAsync(a => a.AttachAsync(scene.InstructorUserId, scene.CourseId, scene.SessionId, assetId, "  บันทึกพิเศษ  ", CancellationToken.None));

        await using var scope = _factory.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var episode = await db.CourseEpisodes().AsNoTracking().SingleAsync(e => e.CourseId == scene.CourseId);
        Assert.Equal("บันทึกพิเศษ", episode.Title);
    }

    [Fact]
    public async Task AttachAsync_AnotherInstructorsId_IsForbidden_AndNothingIsAttached()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromHours(5));
        var other = await CreateSceneAsync(TimeSpan.FromHours(5));
        var assetId = await CreateAssetAsync(other.InstructorUserId);

        var attached = await WithAttacherAsync(a => a.AttachAsync(other.InstructorUserId, scene.CourseId, scene.SessionId, assetId, null, CancellationToken.None));

        Assert.Equal("forbidden", attached.Error.Code);
        var session = await WithAttacherAsync(a => a.GetAsync(scene.SessionId, CancellationToken.None));
        Assert.False(session!.HasRecording);
    }

    [Fact]
    public async Task AttachAsync_AnAssetOfSomeoneElse_IsForbidden()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromHours(5));
        var other = await CreateSceneAsync(TimeSpan.FromHours(5));
        var foreignAsset = await CreateAssetAsync(other.InstructorUserId);

        var attached = await WithAttacherAsync(a => a.AttachAsync(scene.InstructorUserId, scene.CourseId, scene.SessionId, foreignAsset, null, CancellationToken.None));

        Assert.Equal("forbidden", attached.Error.Code);
    }

    [Fact]
    public async Task AttachAsync_AnAssetStillProcessing_IsRefusedWithTheStableReason()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromHours(5));
        var assetId = await CreateAssetAsync(scene.InstructorUserId, status: "Processing");

        var attached = await WithAttacherAsync(a => a.AttachAsync(scene.InstructorUserId, scene.CourseId, scene.SessionId, assetId, null, CancellationToken.None));

        Assert.Equal("conflict", attached.Error.Code);
        Assert.Equal(LiveRecordingAttachReasons.AssetNotReady, attached.Error.Reason);
    }

    [Fact]
    public async Task AttachAsync_ASessionThatHasNotStarted_IsRefusedWithTheStableReason()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromHours(-3)); // starts in three hours
        var assetId = await CreateAssetAsync(scene.InstructorUserId);

        var attached = await WithAttacherAsync(a => a.AttachAsync(scene.InstructorUserId, scene.CourseId, scene.SessionId, assetId, null, CancellationToken.None));

        Assert.Equal("conflict", attached.Error.Code);
        Assert.Equal(LiveRecordingAttachReasons.SessionNotStarted, attached.Error.Reason);
    }

    [Fact]
    public async Task AttachAsync_AnAssetAlreadyUsedByAnotherLesson_IsRefusedWithTheStableReason()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromHours(30));
        var assetId = await CreateAssetAsync(scene.InstructorUserId);
        await WithAttacherAsync(a => a.AttachAsync(scene.InstructorUserId, scene.CourseId, scene.SessionId, assetId, null, CancellationToken.None));

        // A second class of the same course, then the same asset offered for it.
        Guid secondSessionId;
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = await db.Courses().Include(c => c.LiveSessions).SingleAsync(c => c.Id == scene.CourseId);
            var start = DateTime.UtcNow.AddHours(-5);
            var second = course.AddLiveSession("คาบที่สอง", null, start, start.AddHours(2), new SetupClock(DateTime.UtcNow.AddDays(-10)));
            db.Entry(second).State = EntityState.Added;
            await db.SaveChangesAsync();
            secondSessionId = second.Id;
        }

        var attached = await WithAttacherAsync(a => a.AttachAsync(scene.InstructorUserId, scene.CourseId, secondSessionId, assetId, null, CancellationToken.None));

        Assert.Equal("conflict", attached.Error.Code);
        Assert.Equal(LiveRecordingAttachReasons.AssetInUse, attached.Error.Reason);
    }

    [Fact]
    public async Task AttachAsync_AnArchivedCourse_IsRefused()
    {
        var scene = await CreateSceneAsync(TimeSpan.FromHours(5), (course, _, _) => course.Archive());
        var assetId = await CreateAssetAsync(scene.InstructorUserId);

        var attached = await WithAttacherAsync(a => a.AttachAsync(scene.InstructorUserId, scene.CourseId, scene.SessionId, assetId, null, CancellationToken.None));

        Assert.True(attached.IsFailure);
        Assert.Equal("conflict", attached.Error.Code);
    }

    [Fact]
    public async Task AttachAsync_EmptyIds_AreValidationFailures()
    {
        var attached = await WithAttacherAsync(a => a.AttachAsync(Guid.Empty, Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), null, CancellationToken.None));

        Assert.Equal("validation", attached.Error.Code);
    }
}
