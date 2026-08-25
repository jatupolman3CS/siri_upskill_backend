using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Learning;

public sealed class CertificateServiceTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private sealed class FakeCertificateRepository : ICertificateRepository
    {
        public readonly Dictionary<Guid, CERTIFICATE> Certificates = [];

        public Task<CERTIFICATE?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Certificates.TryGetValue(id, out var cert) ? cert : null);

        public Task<CERTIFICATE?> GetByEnrollmentIdAsync(Guid enrollmentId, CancellationToken cancellationToken) =>
            Task.FromResult(Certificates.Values.FirstOrDefault(c => c.ENROLLMENT_ID == enrollmentId));

        public Task<CERTIFICATE?> GetByVerifyCodeAsync(string verifyCode, CancellationToken cancellationToken) =>
            Task.FromResult(Certificates.Values.FirstOrDefault(c => c.VERIFY_CODE == verifyCode));

        public IQueryable<CERTIFICATE> Query() => Certificates.Values.AsQueryable();

        public void Add(CERTIFICATE certificate) => Certificates[certificate.CERTIFICATE_ID] = certificate;

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

    private sealed class FakeCatalogPriceContract : ICatalogPriceContract
    {
        public Task<IReadOnlyDictionary<Guid, CoursePriceInfo>> GetPublishedCoursePricesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, CoursePriceInfo>>(new Dictionary<Guid, CoursePriceInfo>());

        public Task<bool> IsEpisodeFreePreviewAsync(Guid episodeId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<Guid?> GetCourseIdForEpisodeAsync(Guid episodeId, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);

        public Task<bool> IsInstructorOwnerOfEpisodeAsync(Guid episodeId, Guid instructorUserId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> IsInstructorOwnerOfCourseAsync(Guid courseId, Guid instructorUserId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<int> GetPendingReviewsCountAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<IReadOnlyDictionary<Guid, string>> GetCourseTitlesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken)
        {
            var dict = courseIds.ToDictionary(id => id, id => "Fullstack Web Development with Angular & .NET");
            return Task.FromResult<IReadOnlyDictionary<Guid, string>>(dict);
        }
    }

    private sealed class FakeUserContactReader : IUserContactReader
    {
        public Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<string?>("learner@siriupskill.com");

        public Task<(string? Email, string? DisplayName)> GetUserContactInfoAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<(string?, string?)>(("learner@siriupskill.com", "Somchai Jaidee"));
    }

    [Fact]
    public async Task CreateAsync_IssuesCertificateWithSerialAndVerifyCode()
    {
        var certRepo = new FakeCertificateRepository();
        var enrollmentRepo = new FakeEnrollmentRepository();
        var catalog = new FakeCatalogPriceContract();
        var identity = new FakeUserContactReader();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var enrollment = ENROLLMENT.Create(userId, courseId, null, EnrollmentSource.Purchase, null, clock);
        enrollment.Complete(clock);
        enrollmentRepo.Add(enrollment);

        var service = new CertificateService(certRepo, enrollmentRepo, catalog, identity, clock);
        var command = new IssueCertificateCommand(enrollment.ENROLLMENT_ID, "storage/certs/cert-1.pdf");

        var result = await service.CreateAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.StartsWith("CERT-2026-", result.Value.SerialNo);
        Assert.False(string.IsNullOrWhiteSpace(result.Value.VerifyCode));
        Assert.Equal(now, result.Value.IssuedAtUtc);
        Assert.Single(certRepo.Certificates);
    }

    [Fact]
    public async Task VerifyByCodeAsync_WhenValid_ReturnsSuccessAndValidStatus()
    {
        var certRepo = new FakeCertificateRepository();
        var enrollmentRepo = new FakeEnrollmentRepository();
        var catalog = new FakeCatalogPriceContract();
        var identity = new FakeUserContactReader();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var enrollment = ENROLLMENT.Create(userId, courseId, null, EnrollmentSource.Purchase, null, clock);
        enrollmentRepo.Add(enrollment);

        var cert = CERTIFICATE.Create(enrollment.ENROLLMENT_ID, "CERT-2026-ABCDEF12", "VERIFY123456", null, clock);
        certRepo.Add(cert);

        var service = new CertificateService(certRepo, enrollmentRepo, catalog, identity, clock);

        var result = await service.VerifyByCodeAsync("VERIFY123456", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsValid);
        Assert.Equal("CERT-2026-ABCDEF12", result.Value.SerialNo);
        Assert.Equal("Somchai Jaidee", result.Value.LearnerName);
        Assert.Equal("Fullstack Web Development with Angular & .NET", result.Value.CourseTitle);
        Assert.Null(result.Value.RevokedAtUtc);
    }

    [Fact]
    public async Task GeneratePdfAsync_GeneratesValidPdfBytes()
    {
        var certRepo = new FakeCertificateRepository();
        var enrollmentRepo = new FakeEnrollmentRepository();
        var catalog = new FakeCatalogPriceContract();
        var identity = new FakeUserContactReader();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var enrollment = ENROLLMENT.Create(userId, courseId, null, EnrollmentSource.Purchase, null, clock);
        enrollmentRepo.Add(enrollment);

        var cert = CERTIFICATE.Create(enrollment.ENROLLMENT_ID, "CERT-2026-ABCDEF12", "VERIFY123456", null, clock);
        certRepo.Add(cert);

        var service = new CertificateService(certRepo, enrollmentRepo, catalog, identity, clock);

        var result = await service.GeneratePdfAsync(cert.CERTIFICATE_ID, userId, false, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotNull(result.Value.Bytes);
        Assert.NotEmpty(result.Value.Bytes);
        // PDF header "%PDF-"
        Assert.Equal(0x25, result.Value.Bytes[0]);
        Assert.Equal(0x50, result.Value.Bytes[1]);
        Assert.Equal(0x44, result.Value.Bytes[2]);
        Assert.Equal(0x46, result.Value.Bytes[3]);
        Assert.Equal("Certificate-CERT-2026-ABCDEF12.pdf", result.Value.FileName);
    }

    [Fact]
    public async Task RevokeAsync_MarksCertificateRevoked()
    {
        var certRepo = new FakeCertificateRepository();
        var enrollmentRepo = new FakeEnrollmentRepository();
        var catalog = new FakeCatalogPriceContract();
        var identity = new FakeUserContactReader();
        var now = new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);

        var cert = CERTIFICATE.Create(Guid.NewGuid(), "CERT-2026-ABCDEF12", "VERIFY123456", null, clock);
        certRepo.Add(cert);

        var service = new CertificateService(certRepo, enrollmentRepo, catalog, identity, clock);

        var revokeResult = await service.RevokeAsync(cert.CERTIFICATE_ID, CancellationToken.None);
        Assert.True(revokeResult.IsSuccess);

        var verifyResult = await service.VerifyByCodeAsync("VERIFY123456", CancellationToken.None);
        Assert.True(verifyResult.IsSuccess);
        Assert.False(verifyResult.Value.IsValid);
        Assert.Equal(now, verifyResult.Value.RevokedAtUtc);
    }
}
