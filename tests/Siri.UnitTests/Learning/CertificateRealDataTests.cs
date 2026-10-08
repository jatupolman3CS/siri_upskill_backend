using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Learning;

/// <summary>
/// "Real data only" behavior of <see cref="CertificateService"/>: the printed learner name, course title and
/// QR verify URL come from the account / course / configuration, and when one is genuinely unavailable the
/// operation fails instead of printing a generic stand-in.
/// </summary>
public sealed class CertificateRealDataTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private sealed class Contacts(string? email, string? displayName) : IUserContactReader
    {
        public Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult(email);

        public Task<(string? Email, string? DisplayName)> GetUserContactInfoAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult((email, displayName));
    }

    private sealed class Titles(string? title) : ICatalogPriceContract
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
            IReadOnlyDictionary<Guid, string> result = title is null
                ? new Dictionary<Guid, string>()
                : courseIds.ToDictionary(id => id, _ => title);
            return Task.FromResult(result);
        }

        public Task<IReadOnlyDictionary<Guid, decimal>> GetInstructorRevenueSharePercentsAsync(IEnumerable<Guid> instructorIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>());
    }

    private sealed class Certificates : ICertificateRepository
    {
        public readonly Dictionary<Guid, CERTIFICATE> Items = [];

        public Task<CERTIFICATE?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.TryGetValue(id, out var cert) ? cert : null);

        public Task<CERTIFICATE?> GetByEnrollmentIdAsync(Guid enrollmentId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Values.FirstOrDefault(c => c.ENROLLMENT_ID == enrollmentId));

        public Task<CERTIFICATE?> GetByVerifyCodeAsync(string verifyCode, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Values.FirstOrDefault(c => c.VERIFY_CODE == verifyCode));

        public IQueryable<CERTIFICATE> Query() => Items.Values.AsQueryable();

        public void Add(CERTIFICATE certificate) => Items[certificate.CERTIFICATE_ID] = certificate;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class Enrollments : IEnrollmentRepository
    {
        public readonly Dictionary<Guid, ENROLLMENT> Items = [];

        public Task<ENROLLMENT?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.TryGetValue(id, out var enrollment) ? enrollment : null);

        public Task<ENROLLMENT?> GetByUserAndCourseAsync(Guid userId, Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.Values.FirstOrDefault(e => e.USER_ID == userId && e.COURSE_ID == courseId));

        public IQueryable<ENROLLMENT> Query() => Items.Values.AsQueryable();

        public void Add(ENROLLMENT enrollment) => Items[enrollment.ENROLLMENT_ID] = enrollment;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static (CertificateService Service, CERTIFICATE Cert, ENROLLMENT Enrollment, Guid UserId) Arrange(
        string? email,
        string? displayName,
        string? courseTitle,
        CertificateOptions? options = null)
    {
        var clock = new FakeClock(new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc));
        var certs = new Certificates();
        var enrollments = new Enrollments();

        var userId = Guid.NewGuid();
        var enrollment = ENROLLMENT.Create(userId, Guid.NewGuid(), null, EnrollmentSource.Purchase, null, clock);
        enrollments.Add(enrollment);

        var cert = CERTIFICATE.Create(enrollment.ENROLLMENT_ID, "CERT-2026-REAL0001", "VERIFYREAL01", null, clock);
        certs.Add(cert);

        var service = CertificateServiceFactory.Create(certs, enrollments, new Titles(courseTitle), new Contacts(email, displayName), clock, options);
        return (service, cert, enrollment, userId);
    }

    [Fact]
    public async Task BuildPdfDataAsync_PrintsRealLearnerNameCourseTitleAndConfiguredVerifyUrl()
    {
        var (service, cert, enrollment, _) = Arrange("somchai@example.test", "สมชาย ใจดี", "คอร์สจริงของระบบ");

        var result = await service.BuildPdfDataAsync(cert, enrollment, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("สมชาย ใจดี", result.Value.StudentName);
        Assert.Equal("คอร์สจริงของระบบ", result.Value.CourseTitle);
        Assert.Equal($"{CertificateServiceFactory.PublicBaseUrl}/certificates/verify/VERIFYREAL01", result.Value.VerifyUrl);
        Assert.DoesNotContain("siriupskill.com", result.Value.VerifyUrl);
    }

    [Fact]
    public async Task BuildPdfDataAsync_TrailingSlashInConfiguredOrigin_DoesNotDoubleTheSlash()
    {
        var (service, cert, enrollment, _) = Arrange("a@example.test", "Learner", "Course",
            new CertificateOptions { PublicBaseUrl = " https://learn.example.test/ " });

        var result = await service.BuildPdfDataAsync(cert, enrollment, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("https://learn.example.test/certificates/verify/VERIFYREAL01", result.Value.VerifyUrl);
    }

    [Fact]
    public async Task BuildPdfDataAsync_BlankDisplayName_UsesRealEmailNotAGenericName()
    {
        var (service, cert, enrollment, _) = Arrange("somchai@example.test", "  ", "Course");

        var result = await service.BuildPdfDataAsync(cert, enrollment, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("somchai@example.test", result.Value.StudentName);
    }

    [Fact]
    public async Task BuildPdfDataAsync_LearnerUnresolvable_FailsWithNotFound()
    {
        var (service, cert, enrollment, _) = Arrange(null, null, "Course");

        var result = await service.BuildPdfDataAsync(cert, enrollment, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task BuildPdfDataAsync_CourseTitleUnresolvable_FailsWithNotFound()
    {
        var (service, cert, enrollment, _) = Arrange("a@example.test", "Learner", null);

        var result = await service.BuildPdfDataAsync(cert, enrollment, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("learn.example.test")]
    [InlineData("ftp://learn.example.test")]
    public async Task BuildPdfDataAsync_PublicBaseUrlMissingOrInvalid_FailsWith503Code(string baseUrl)
    {
        var (service, cert, enrollment, _) = Arrange("a@example.test", "Learner", "Course",
            new CertificateOptions { PublicBaseUrl = baseUrl });

        var result = await service.BuildPdfDataAsync(cert, enrollment, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CertificateService.PublicUrlNotConfiguredCode, result.Error.Code);
        Assert.EndsWith(DomainErrorHttpResults.NotConfiguredCodeSuffix, result.Error.Code);
    }

    [Fact]
    public async Task GeneratePdfAsync_LearnerUnresolvable_ReturnsFailureNotAPdfWithAPlaceholder()
    {
        var (service, cert, _, userId) = Arrange(null, null, "Course");

        var result = await service.GeneratePdfAsync(cert.CERTIFICATE_ID, userId, false, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task GeneratePdfByVerifyCodeAsync_PublicBaseUrlNotConfigured_ReturnsFailure()
    {
        var (service, cert, _, _) = Arrange("a@example.test", "Learner", "Course", new CertificateOptions());

        var result = await service.GeneratePdfByVerifyCodeAsync(cert.VERIFY_CODE, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(CertificateService.PublicUrlNotConfiguredCode, result.Error.Code);
    }

    [Fact]
    public async Task GetOwnAsync_LearnerUnresolvable_ReturnsFailureInsteadOfAGenericName()
    {
        var (service, cert, _, userId) = Arrange(null, null, "Course");

        var result = await service.GetOwnAsync(userId, cert.CERTIFICATE_ID, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("not_found", result.Error.Code);
    }

    [Fact]
    public async Task GetOwnAsync_Resolvable_ReturnsRealNameAndTitle()
    {
        var (service, cert, _, userId) = Arrange("a@example.test", "สมชาย ใจดี", "คอร์สจริง");

        var result = await service.GetOwnAsync(userId, cert.CERTIFICATE_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("สมชาย ใจดี", result.Value.LearnerName);
        Assert.Equal("คอร์สจริง", result.Value.CourseTitle);
    }

    private static string ResolveEffectiveBaseUrl(Dictionary<string, string?> settings)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLearningModule();

        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<IOptions<CertificateOptions>>().Value.PublicBaseUrl;
    }

    [Fact]
    public void LearningModule_ExplicitCertificatePublicBaseUrl_WinsOverTheSiteWideOrigin()
    {
        var effective = ResolveEffectiveBaseUrl(new Dictionary<string, string?>
        {
            ["Learning:Certificates:PublicBaseUrl"] = "https://certs.example.test",
            ["Seo:PublicBaseUrl"] = "https://site.example.test",
        });

        Assert.Equal("https://certs.example.test", effective);
    }

    [Fact]
    public void LearningModule_CertificateOriginUnset_FallsBackToTheSiteWideSeoOrigin()
    {
        var effective = ResolveEffectiveBaseUrl(new Dictionary<string, string?>
        {
            ["Seo:PublicBaseUrl"] = "https://site.example.test",
        });

        Assert.Equal("https://site.example.test", effective);
    }

    [Fact]
    public void LearningModule_NoOriginConfiguredAnywhere_StaysEmptyRatherThanInventingADomain()
    {
        var effective = ResolveEffectiveBaseUrl([]);

        Assert.Equal(string.Empty, effective);
        Assert.Null(new CertificateOptions { PublicBaseUrl = effective }.GetNormalizedPublicBaseUrl());
    }

    [Fact]
    public async Task VerifyByCodeAsync_BlankDisplayName_DoesNotLeakEmailOnThePublicLookup()
    {
        var (service, cert, _, _) = Arrange("private@example.test", " ", "Course");

        var result = await service.VerifyByCodeAsync(cert.VERIFY_CODE, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Null(result.Value.LearnerName);
    }
}
