using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Learning;

public sealed class EpisodeProgressServiceTests
{
    private sealed class FakeCatalog(Guid? courseId) : ICatalogPriceContract
    {
        public Task<Guid?> GetCourseIdForEpisodeAsync(Guid episodeId, CancellationToken ct) => Task.FromResult(courseId);
        public Task<IReadOnlyDictionary<Guid, CoursePriceInfo>> GetPublishedCoursePricesAsync(IEnumerable<Guid> ids, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> IsEpisodeFreePreviewAsync(Guid id, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> IsInstructorOwnerOfEpisodeAsync(Guid id, Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<bool> IsInstructorOwnerOfCourseAsync(Guid id, Guid userId, CancellationToken ct) => throw new NotSupportedException();
        public Task<int> GetPendingReviewsCountAsync(CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, string>> GetCourseTitlesAsync(IEnumerable<Guid> ids, CancellationToken ct) => throw new NotSupportedException();
        public Task<IReadOnlyDictionary<Guid, decimal>> GetInstructorRevenueSharePercentsAsync(IEnumerable<Guid> ids, CancellationToken ct) => throw new NotSupportedException();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpsertProgress_WhenEpisodeIsMissingOrFromAnotherCourse_DoesNotWrite(bool missing)
    {
        var clock = new FakeClock(DateTime.UtcNow);
        var enrollments = new FakeEnrollmentRepository();
        var progress = new FakeEpisodeProgressRepository();
        var watchEvents = new FakeWatchEventRepository();
        var enrollment = ENROLLMENT.Create(Guid.NewGuid(), Guid.NewGuid(), null, EnrollmentSource.Purchase, null, clock);
        enrollments.Add(enrollment);
        var service = new EpisodeProgressService(progress, enrollments, watchEvents, clock, new FakeCatalog(missing ? null : Guid.NewGuid()));

        var result = await service.UpsertProgressAsync(enrollment.USER_ID, enrollment.ENROLLMENT_ID,
            Guid.NewGuid(), new(100, 100, true), CancellationToken.None);

        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(progress.ProgressList);
        Assert.Empty(watchEvents.Events);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpsertProgress_WhenRevokedOrExpired_DoesNotWrite(bool revoked)
    {
        var clock = new FakeClock(DateTime.UtcNow);
        var enrollments = new FakeEnrollmentRepository();
        var progress = new FakeEpisodeProgressRepository();
        var watchEvents = new FakeWatchEventRepository();
        var enrollment = ENROLLMENT.Create(Guid.NewGuid(), Guid.NewGuid(), null, EnrollmentSource.Purchase,
            revoked ? null : clock.UtcNow, clock);
        if (revoked) enrollment.Revoke();
        enrollments.Add(enrollment);
        var service = new EpisodeProgressService(progress, enrollments, watchEvents, clock, new FakeCatalog(enrollment.COURSE_ID));

        var result = await service.UpsertProgressAsync(enrollment.USER_ID, enrollment.ENROLLMENT_ID,
            Guid.NewGuid(), new(100, 100, true), CancellationToken.None);

        Assert.Equal("forbidden", result.Error.Code);
        Assert.Empty(progress.ProgressList);
        Assert.Empty(watchEvents.Events);
    }

    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private sealed class FakeEpisodeProgressRepository : IEpisodeProgressRepository
    {
        public readonly List<EPISODE_PROGRESS> ProgressList = [];

        public Task<EPISODE_PROGRESS?> GetByEnrollmentAndEpisodeAsync(Guid enrollmentId, Guid episodeId, CancellationToken cancellationToken) =>
            Task.FromResult(ProgressList.FirstOrDefault(p => p.ENROLLMENT_ID == enrollmentId && p.EPISODE_ID == episodeId));

        public Task<IReadOnlyList<EPISODE_PROGRESS>> ListForEnrollmentAsync(Guid enrollmentId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EPISODE_PROGRESS>>(ProgressList.Where(p => p.ENROLLMENT_ID == enrollmentId).ToList());

        public void Add(EPISODE_PROGRESS episodeProgress) => ProgressList.Add(episodeProgress);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeEnrollmentRepository : IEnrollmentRepository
    {
        public readonly Dictionary<Guid, ENROLLMENT> Enrollments = [];

        public Task<ENROLLMENT?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Enrollments.TryGetValue(id, out var enrollment) ? enrollment : null);

        public Task<ENROLLMENT?> GetByUserAndCourseAsync(Guid userId, Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult(Enrollments.Values.FirstOrDefault(e => e.USER_ID == userId && e.COURSE_ID == courseId));

        public IQueryable<ENROLLMENT> Query() => Enrollments.Values.AsQueryable();

        public void Add(ENROLLMENT enrollment) => Enrollments[enrollment.ENROLLMENT_ID] = enrollment;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeWatchEventRepository : IWatchEventRepository
    {
        public readonly List<WATCH_EVENT> Events = [];

        public IQueryable<WATCH_EVENT> Query() => Events.AsQueryable();

        public void Append(WATCH_EVENT watchEvent) => Events.Add(watchEvent);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task UpsertProgressAsync_WhenFirstTime_CreatesProgressRowAndAppendsHeartbeatEvent()
    {
        var progressRepo = new FakeEpisodeProgressRepository();
        var enrollmentRepo = new FakeEnrollmentRepository();
        var watchEventsRepo = new FakeWatchEventRepository();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();

        var enrollment = ENROLLMENT.Create(userId, courseId, null, EnrollmentSource.Purchase, null, clock);
        enrollmentRepo.Add(enrollment);

        var service = new EpisodeProgressService(progressRepo, enrollmentRepo, watchEventsRepo, clock, new FakeCatalog(courseId));
        var command = new UpsertEpisodeProgressCommand(120, 120, false);

        var result = await service.UpsertProgressAsync(userId, enrollment.ENROLLMENT_ID, episodeId, command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(120, result.Value.LastPositionSeconds);
        Assert.Equal(120, result.Value.WatchedSeconds);
        Assert.False(result.Value.IsCompleted);
        Assert.Null(result.Value.CompletedAtUtc);
        Assert.Single(progressRepo.ProgressList);

        // Asserts WATCH_EVENT is appended with Heartbeat
        var appended = Assert.Single(watchEventsRepo.Events);
        Assert.Equal(enrollment.ENROLLMENT_ID, appended.ENROLLMENT_ID);
        Assert.Equal(episodeId, appended.EPISODE_ID);
        Assert.Equal(WatchEventType.Heartbeat, appended.EVENT_TYPE);
        Assert.Equal(120, appended.POSITION_SECONDS);
        Assert.Equal(now, appended.OCCURRED_AT_UTC);
    }

    [Fact]
    public async Task UpsertProgressAsync_WhenCompleted_SetsCompletedAtUtcAndAppendsEndedEvent()
    {
        var progressRepo = new FakeEpisodeProgressRepository();
        var enrollmentRepo = new FakeEnrollmentRepository();
        var watchEventsRepo = new FakeWatchEventRepository();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();

        var enrollment = ENROLLMENT.Create(userId, courseId, null, EnrollmentSource.Purchase, null, clock);
        enrollmentRepo.Add(enrollment);

        var service = new EpisodeProgressService(progressRepo, enrollmentRepo, watchEventsRepo, clock, new FakeCatalog(courseId));

        // First heartbeat (creates row)
        await service.UpsertProgressAsync(userId, enrollment.ENROLLMENT_ID, episodeId, new UpsertEpisodeProgressCommand(300, 300, false), CancellationToken.None);

        // Second heartbeat: completed (even if delta < 10s)
        var result = await service.UpsertProgressAsync(userId, enrollment.ENROLLMENT_ID, episodeId, new UpsertEpisodeProgressCommand(305, 305, true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsCompleted);
        Assert.Equal(now, result.Value.CompletedAtUtc);

        Assert.Equal(2, watchEventsRepo.Events.Count);
        Assert.Equal(WatchEventType.Heartbeat, watchEventsRepo.Events[0].EVENT_TYPE);
        Assert.Equal(WatchEventType.Ended, watchEventsRepo.Events[1].EVENT_TYPE);
        Assert.Equal(305, watchEventsRepo.Events[1].POSITION_SECONDS);
    }

    [Fact]
    public async Task UpsertProgressAsync_WhenDeltaLessThan10SecondsAndNotCompleted_SkipsProgressTouch_AppendsHeartbeatEvent()
    {
        var progressRepo = new FakeEpisodeProgressRepository();
        var enrollmentRepo = new FakeEnrollmentRepository();
        var watchEventsRepo = new FakeWatchEventRepository();
        var initialTime = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(initialTime);

        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();

        var enrollment = ENROLLMENT.Create(userId, courseId, null, EnrollmentSource.Purchase, null, clock);
        enrollmentRepo.Add(enrollment);

        var service = new EpisodeProgressService(progressRepo, enrollmentRepo, watchEventsRepo, clock, new FakeCatalog(courseId));

        // 1. Initial progress row at position 100
        await service.UpsertProgressAsync(userId, enrollment.ENROLLMENT_ID, episodeId, new UpsertEpisodeProgressCommand(100, 100, false), CancellationToken.None);
        var initialUpdatedAt = progressRepo.ProgressList[0].UPDATED_AT_UTC;

        // 2. Position moved by only 5 seconds (< 10s), not completed
        var laterTime = initialTime.AddSeconds(15);
        var laterClock = new FakeClock(laterTime);
        var laterService = new EpisodeProgressService(progressRepo, enrollmentRepo, watchEventsRepo, laterClock, new FakeCatalog(courseId));

        var result = await laterService.UpsertProgressAsync(userId, enrollment.ENROLLMENT_ID, episodeId, new UpsertEpisodeProgressCommand(105, 105, false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        // EPISODE_PROGRESS row was NOT touched (remains at initial position 100)
        var storedProgress = progressRepo.ProgressList[0];
        Assert.Equal(100, storedProgress.LAST_POSITION_SECONDS);
        Assert.Equal(initialUpdatedAt, storedProgress.UPDATED_AT_UTC);

        // WATCH_EVENT was still appended with Heartbeat at the current position 105
        Assert.Equal(2, watchEventsRepo.Events.Count);
        Assert.Equal(WatchEventType.Heartbeat, watchEventsRepo.Events[1].EVENT_TYPE);
        Assert.Equal(105, watchEventsRepo.Events[1].POSITION_SECONDS);
        Assert.Equal(laterTime, watchEventsRepo.Events[1].OCCURRED_AT_UTC);
    }

    [Fact]
    public async Task UpsertProgressAsync_WhenDeltaLessThan10SecondsAndNotCompleted_ResponseReflectsSubmittedPosition_NotStaleEntity()
    {
        // Regression test for docs/TASKS.md X-29 review: a skipped EPISODE_PROGRESS write must
        // still report the position/watched seconds the client just submitted, not the stale
        // un-Touch()ed row -- otherwise a client that reloads right after a skipped heartbeat
        // sees resume position drift by up to the 10s skip window.
        var progressRepo = new FakeEpisodeProgressRepository();
        var enrollmentRepo = new FakeEnrollmentRepository();
        var watchEventsRepo = new FakeWatchEventRepository();
        var initialTime = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(initialTime);

        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();

        var enrollment = ENROLLMENT.Create(userId, courseId, null, EnrollmentSource.Purchase, null, clock);
        enrollmentRepo.Add(enrollment);

        var service = new EpisodeProgressService(progressRepo, enrollmentRepo, watchEventsRepo, clock, new FakeCatalog(courseId));

        // 1. Initial progress row at position 100, watched 100
        await service.UpsertProgressAsync(userId, enrollment.ENROLLMENT_ID, episodeId, new UpsertEpisodeProgressCommand(100, 100, false), CancellationToken.None);

        // 2. Position moved by only 5 seconds (< 10s), not completed -- write is skipped
        var laterTime = initialTime.AddSeconds(15);
        var laterClock = new FakeClock(laterTime);
        var laterService = new EpisodeProgressService(progressRepo, enrollmentRepo, watchEventsRepo, laterClock, new FakeCatalog(courseId));

        var result = await laterService.UpsertProgressAsync(userId, enrollment.ENROLLMENT_ID, episodeId, new UpsertEpisodeProgressCommand(105, 108, false), CancellationToken.None);

        Assert.True(result.IsSuccess);

        // The DB row is still un-touched (asserted by the sibling test above) -- but the
        // RESPONSE must reflect what the client just submitted, not the stale row at 100.
        Assert.Equal(105, result.Value.LastPositionSeconds);
        Assert.Equal(108, result.Value.WatchedSeconds);
        Assert.False(result.Value.IsCompleted);
        Assert.Null(result.Value.CompletedAtUtc);

        // Sanity: the underlying row genuinely was not persisted with the new position.
        Assert.Equal(100, progressRepo.ProgressList[0].LAST_POSITION_SECONDS);
    }

    [Fact]
    public async Task UpsertProgressAsync_WhenDeltaLessThan10SecondsAfterCompletion_ResponseStaysCompleted()
    {
        // Regression guard for the fix above: once an episode is completed, a later low-delta
        // heartbeat with IsCompleted=false must not report the episode as incomplete again --
        // completion is monotonic (see EPISODE_PROGRESS.Touch), so the response must come from
        // the entity's real IsCompleted/CompletedAtUtc, never echoed straight from the command.
        var progressRepo = new FakeEpisodeProgressRepository();
        var enrollmentRepo = new FakeEnrollmentRepository();
        var watchEventsRepo = new FakeWatchEventRepository();
        var initialTime = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(initialTime);

        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();

        var enrollment = ENROLLMENT.Create(userId, courseId, null, EnrollmentSource.Purchase, null, clock);
        enrollmentRepo.Add(enrollment);

        var service = new EpisodeProgressService(progressRepo, enrollmentRepo, watchEventsRepo, clock, new FakeCatalog(courseId));

        // 1. Episode completes.
        await service.UpsertProgressAsync(userId, enrollment.ENROLLMENT_ID, episodeId, new UpsertEpisodeProgressCommand(300, 300, true), CancellationToken.None);
        var completedAt = progressRepo.ProgressList[0].COMPLETED_AT_UTC;

        // 2. A later heartbeat still fires (e.g. learner rewatching) with IsCompleted=false and
        // a small position delta -- write is skipped.
        var laterTime = initialTime.AddSeconds(15);
        var laterClock = new FakeClock(laterTime);
        var laterService = new EpisodeProgressService(progressRepo, enrollmentRepo, watchEventsRepo, laterClock, new FakeCatalog(courseId));

        var result = await laterService.UpsertProgressAsync(userId, enrollment.ENROLLMENT_ID, episodeId, new UpsertEpisodeProgressCommand(303, 303, false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsCompleted);
        Assert.Equal(completedAt, result.Value.CompletedAtUtc);
        Assert.Equal(303, result.Value.LastPositionSeconds);
    }

    [Fact]
    public async Task UpsertProgressAsync_WhenDeltaGreaterOrEqualTo10Seconds_TouchesProgress_AppendsHeartbeatEvent()
    {
        var progressRepo = new FakeEpisodeProgressRepository();
        var enrollmentRepo = new FakeEnrollmentRepository();
        var watchEventsRepo = new FakeWatchEventRepository();
        var initialTime = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(initialTime);

        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();

        var enrollment = ENROLLMENT.Create(userId, courseId, null, EnrollmentSource.Purchase, null, clock);
        enrollmentRepo.Add(enrollment);

        var service = new EpisodeProgressService(progressRepo, enrollmentRepo, watchEventsRepo, clock, new FakeCatalog(courseId));

        // 1. Initial position 100
        await service.UpsertProgressAsync(userId, enrollment.ENROLLMENT_ID, episodeId, new UpsertEpisodeProgressCommand(100, 100, false), CancellationToken.None);

        // 2. Position moved by 15 seconds (>= 10s)
        var laterTime = initialTime.AddSeconds(15);
        var laterClock = new FakeClock(laterTime);
        var laterService = new EpisodeProgressService(progressRepo, enrollmentRepo, watchEventsRepo, laterClock, new FakeCatalog(courseId));

        var result = await laterService.UpsertProgressAsync(userId, enrollment.ENROLLMENT_ID, episodeId, new UpsertEpisodeProgressCommand(115, 115, false), CancellationToken.None);

        Assert.True(result.IsSuccess);
        // EPISODE_PROGRESS row was touched and updated to 115
        var storedProgress = progressRepo.ProgressList[0];
        Assert.Equal(115, storedProgress.LAST_POSITION_SECONDS);
        Assert.Equal(laterTime, storedProgress.UPDATED_AT_UTC);

        // WATCH_EVENT was appended with Heartbeat
        Assert.Equal(2, watchEventsRepo.Events.Count);
        Assert.Equal(WatchEventType.Heartbeat, watchEventsRepo.Events[1].EVENT_TYPE);
        Assert.Equal(115, watchEventsRepo.Events[1].POSITION_SECONDS);
    }

    [Fact]
    public async Task UpsertProgressAsync_WithDifferentUser_ReturnsNotFound()
    {
        var progressRepo = new FakeEpisodeProgressRepository();
        var enrollmentRepo = new FakeEnrollmentRepository();
        var watchEventsRepo = new FakeWatchEventRepository();
        var clock = new FakeClock(DateTime.UtcNow);

        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var enrollment = ENROLLMENT.Create(ownerId, Guid.NewGuid(), null, EnrollmentSource.Purchase, null, clock);
        enrollmentRepo.Add(enrollment);

        var service = new EpisodeProgressService(progressRepo, enrollmentRepo, watchEventsRepo, clock, new FakeCatalog(enrollment.COURSE_ID));
        var command = new UpsertEpisodeProgressCommand(100, 100, false);

        var result = await service.UpsertProgressAsync(otherUserId, enrollment.ENROLLMENT_ID, Guid.NewGuid(), command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("not_found", result.Error.Code);
        Assert.Empty(watchEventsRepo.Events);
    }
}
