using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Learning;

public sealed class EnrollmentServiceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UpdateProgress_WhenAccessEnded_DoesNotGrantCertificate(bool revoked)
    {
        var clock = new FakeClock(DateTime.UtcNow);
        var repo = new FakeEnrollmentRepository();
        var certificates = new FakeCertificateRepository();
        var enrollment = ENROLLMENT.Create(Guid.NewGuid(), Guid.NewGuid(), null, EnrollmentSource.Purchase,
            revoked ? null : clock.UtcNow, clock);
        if (revoked) enrollment.Revoke();
        repo.Add(enrollment);
        var service = new EnrollmentService(repo, certificates, clock, new FakeCourseSummaryReader());

        var result = await service.UpdateOwnProgressAsync(enrollment.USER_ID, enrollment.ENROLLMENT_ID,
            new(100m), CancellationToken.None);

        Assert.Equal("forbidden", result.Error.Code);
        Assert.Equal(0m, enrollment.PROGRESS_PERCENT);
        Assert.Empty(certificates.Certificates);
    }

    [Theory]
    [InlineData(EnrollmentStatus.Active)]
    [InlineData(EnrollmentStatus.Expired)]
    [InlineData(EnrollmentStatus.Revoked)]
    public async Task CreateAsync_WhenAccessHasEnded_ReactivatesExistingRowAndPreservesProgress(EnrollmentStatus status)
    {
        var now = new DateTime(2026, 9, 7, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);
        var repo = new FakeEnrollmentRepository();
        var service = new EnrollmentService(repo, new FakeCertificateRepository(), clock, new FakeCourseSummaryReader());
        var existing = ENROLLMENT.Create(Guid.NewGuid(), Guid.NewGuid(), null, EnrollmentSource.Purchase, now, clock);
        existing.UpdateProgress(42m, clock);
        if (status == EnrollmentStatus.Expired) existing.Expire();
        if (status == EnrollmentStatus.Revoked) existing.Revoke();
        repo.Add(existing);
        var newOrderId = Guid.NewGuid();

        var result = await service.CreateAsync(new(existing.USER_ID, existing.COURSE_ID, newOrderId, EnrollmentSource.Purchase, now.AddDays(30)), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(repo.Enrollments);
        Assert.Equal(existing.ENROLLMENT_ID, result.Value.Id);
        Assert.Equal(EnrollmentStatus.Active, result.Value.Status);
        Assert.Equal(newOrderId, result.Value.OrderId);
        Assert.Equal(42m, result.Value.ProgressPercent);
    }

    private sealed class FakeCourseSummaryReader : ICourseSummaryReader
    {
        public Task<IReadOnlyDictionary<Guid, CourseSummaryInfo>> GetCourseSummariesAsync(
            IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, CourseSummaryInfo>>(new Dictionary<Guid, CourseSummaryInfo>());
    }

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

    private sealed class FakeCertificateRepository : ICertificateRepository
    {
        public readonly Dictionary<Guid, CERTIFICATE> Certificates = [];

        public Task<CERTIFICATE?> GetByIdAsync(Guid certificateId, CancellationToken cancellationToken) =>
            Task.FromResult(Certificates.TryGetValue(certificateId, out var c) ? c : null);

        public Task<CERTIFICATE?> GetByEnrollmentIdAsync(Guid enrollmentId, CancellationToken cancellationToken) =>
            Task.FromResult(Certificates.Values.FirstOrDefault(c => c.ENROLLMENT_ID == enrollmentId));

        public Task<CERTIFICATE?> GetByVerifyCodeAsync(string verifyCode, CancellationToken cancellationToken) =>
            Task.FromResult(Certificates.Values.FirstOrDefault(c => c.VERIFY_CODE == verifyCode));

        public IQueryable<CERTIFICATE> Query() => Certificates.Values.AsQueryable();

        public void Add(CERTIFICATE certificate) => Certificates[certificate.CERTIFICATE_ID] = certificate;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task CreateAsync_WhenNotEnrolled_CreatesActiveEnrollment()
    {
        var repo = new FakeEnrollmentRepository();
        var certRepo = new FakeCertificateRepository();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);
        var service = new EnrollmentService(repo, certRepo, clock, new FakeCourseSummaryReader());

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
        var certRepo = new FakeCertificateRepository();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new EnrollmentService(repo, certRepo, clock, new FakeCourseSummaryReader());

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
        var certRepo = new FakeCertificateRepository();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new EnrollmentService(repo, certRepo, clock, new FakeCourseSummaryReader());

        var ownerId = Guid.NewGuid();
        var otherUserId = Guid.NewGuid();
        var enrollment = ENROLLMENT.Create(ownerId, Guid.NewGuid(), null, EnrollmentSource.Purchase, null, clock);
        repo.Add(enrollment);

        var result = await service.GetOwnAsync(otherUserId, enrollment.ENROLLMENT_ID, CancellationToken.None);

        Assert.False(result.IsSuccess);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task UpdateOwnProgressAsync_WhenReaches100_SetsCompletedAtUtcAndAutoIssuesCertificate()
    {
        var repo = new FakeEnrollmentRepository();
        var certRepo = new FakeCertificateRepository();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);
        var service = new EnrollmentService(repo, certRepo, clock, new FakeCourseSummaryReader());

        var userId = Guid.NewGuid();
        var enrollment = ENROLLMENT.Create(userId, Guid.NewGuid(), null, EnrollmentSource.Purchase, null, clock);
        repo.Add(enrollment);

        var command = new UpdateEnrollmentProgressCommand(100m);
        var result = await service.UpdateOwnProgressAsync(userId, enrollment.ENROLLMENT_ID, command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(100m, result.Value.ProgressPercent);
        Assert.Equal(now, result.Value.CompletedAtUtc);

        Assert.Single(certRepo.Certificates);
        var cert = certRepo.Certificates.Values.First();
        Assert.Equal(enrollment.ENROLLMENT_ID, cert.ENROLLMENT_ID);
        Assert.StartsWith("CERT-2026-", cert.SERIAL_NO);
    }
}
