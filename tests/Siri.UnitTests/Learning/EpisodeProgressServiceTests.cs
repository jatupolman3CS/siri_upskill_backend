using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Learning;

public sealed class EpisodeProgressServiceTests
{
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

    [Fact]
    public async Task UpsertProgressAsync_WhenFirstTime_CreatesProgressRow()
    {
        var progressRepo = new FakeEpisodeProgressRepository();
        var enrollmentRepo = new FakeEnrollmentRepository();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();

        var enrollment = ENROLLMENT.Create(userId, courseId, null, EnrollmentSource.Purchase, null, clock);
        enrollmentRepo.Add(enrollment);

        var service = new EpisodeProgressService(progressRepo, enrollmentRepo, clock);
        var command = new UpsertEpisodeProgressCommand(120, 120, false);

        var result = await service.UpsertProgressAsync(userId, enrollment.ENROLLMENT_ID, episodeId, command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(120, result.Value.LastPositionSeconds);
        Assert.Equal(120, result.Value.WatchedSeconds);
        Assert.False(result.Value.IsCompleted);
        Assert.Null(result.Value.CompletedAtUtc);
        Assert.Single(progressRepo.ProgressList);
    }

    [Fact]
    public async Task UpsertProgressAsync_WhenCompleted_SetsCompletedAtUtc()
    {
        var progressRepo = new FakeEpisodeProgressRepository();
        var enrollmentRepo = new FakeEnrollmentRepository();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();

        var enrollment = ENROLLMENT.Create(userId, courseId, null, EnrollmentSource.Purchase, null, clock);
        enrollmentRepo.Add(enrollment);

        var service = new EpisodeProgressService(progressRepo, enrollmentRepo, clock);

        // First heartbeat
        await service.UpsertProgressAsync(userId, enrollment.ENROLLMENT_ID, episodeId, new UpsertEpisodeProgressCommand(300, 300, false), CancellationToken.None);

        // Second heartbeat: completed
        var result = await service.UpsertProgressAsync(userId, enrollment.ENROLLMENT_ID, episodeId, new UpsertEpisodeProgressCommand(600, 600, true), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsCompleted);
        Assert.Equal(now, result.Value.CompletedAtUtc);
    }

    [Fact]
    public async Task UpsertProgressAsync_WithDifferentUser_ReturnsNotFound()
    {
        var progressRepo = new FakeEpisodeProgressRepository();
        var enrollmentRepo = new FakeEnrollmentRepository();
        var clock = new FakeClock(DateTime.UtcNow);

        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var enrollment = ENROLLMENT.Create(ownerId, Guid.NewGuid(), null, EnrollmentSource.Purchase, null, clock);
        enrollmentRepo.Add(enrollment);

        var service = new EpisodeProgressService(progressRepo, enrollmentRepo, clock);
        var command = new UpsertEpisodeProgressCommand(100, 100, false);

        var result = await service.UpsertProgressAsync(otherUserId, enrollment.ENROLLMENT_ID, Guid.NewGuid(), command, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("not_found", result.Error.Code);
    }
}
