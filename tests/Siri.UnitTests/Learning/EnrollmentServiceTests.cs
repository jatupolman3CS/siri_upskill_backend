using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Learning;

public sealed class EnrollmentServiceTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
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
    public async Task CreateAsync_WhenNotEnrolled_CreatesActiveEnrollment()
    {
        var repo = new FakeEnrollmentRepository();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);
        var service = new EnrollmentService(repo, clock);

        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var command = new CreateEnrollmentCommand(userId, courseId, null, EnrollmentSource.Purchase, null);

        var result = await service.CreateAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(userId, result.Value.UserId);
        Assert.Equal(courseId, result.Value.CourseId);
        Assert.Equal(EnrollmentStatus.Active, result.Value.Status);
        Assert.Equal(0m, result.Value.ProgressPercent);
        Assert.Single(repo.Enrollments);
    }

    [Fact]
    public async Task CreateAsync_WhenAlreadyActive_ReturnsConflict()
    {
        var repo = new FakeEnrollmentRepository();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new EnrollmentService(repo, clock);

        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var command = new CreateEnrollmentCommand(userId, courseId, null, EnrollmentSource.Purchase, null);

        await service.CreateAsync(command, CancellationToken.None);
        var duplicateResult = await service.CreateAsync(command, CancellationToken.None);

        Assert.False(duplicateResult.IsSuccess);
        Assert.Equal("conflict", duplicateResult.Error.Code);
    }

    [Fact]
    public async Task GetOwnAsync_WithDifferentUser_ReturnsNotFound()
    {
        var repo = new FakeEnrollmentRepository();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new EnrollmentService(repo, clock);

        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var enrollment = ENROLLMENT.Create(ownerId, Guid.NewGuid(), null, EnrollmentSource.Purchase, null, clock);
        repo.Add(enrollment);

        var result = await service.GetOwnAsync(otherUserId, enrollment.ENROLLMENT_ID, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task UpdateOwnProgressAsync_WhenReaches100_SetsCompletedAtUtc()
    {
        var repo = new FakeEnrollmentRepository();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);
        var service = new EnrollmentService(repo, clock);

        var userId = Guid.NewGuid();
        var enrollment = ENROLLMENT.Create(userId, Guid.NewGuid(), null, EnrollmentSource.Purchase, null, clock);
        repo.Add(enrollment);

        var command = new UpdateEnrollmentProgressCommand(100m);
        var result = await service.UpdateOwnProgressAsync(userId, enrollment.ENROLLMENT_ID, command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(100m, result.Value.ProgressPercent);
        Assert.Equal(now, result.Value.CompletedAtUtc);
    }
}
