using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Learning.Domain;
using Siri.Modules.Learning.Infrastructure.Contracts;
using Siri.SharedKernel;

namespace Siri.UnitTests.Learning;

/// <summary>
/// D2 (integrator-qa): <c>COURSES.ENROLLMENT_COUNT</c> had no writer, so public cards/detail and the instructor dashboard's per-course table showed 0 learners. Catalog now
/// owns one updater (<see cref="ICourseEnrollmentCountUpdater"/>); Learning reports every enrollment transition that starts or stops an enrollment counting. This file proves
/// WHAT Learning reports (the transition table), per path: the admin service, the single-course contract call and the batched purchase call.
/// The number's definition: Active or Expired enrollments, i.e. everyone not revoked — the same rule as the dashboard's <c>totalStudents</c> KPI.
/// </summary>
public sealed class EnrollmentCountTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 10, 0, 0, DateTimeKind.Utc);

    private sealed class FixedClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private sealed class InMemoryEnrollments : IEnrollmentRepository
    {
        public List<ENROLLMENT> Items { get; } = [];

        public int SaveCount { get; private set; }

        public Task<ENROLLMENT?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(e => e.ENROLLMENT_ID == id));

        public Task<ENROLLMENT?> GetByUserAndCourseAsync(Guid userId, Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(e => e.USER_ID == userId && e.COURSE_ID == courseId));

        public IQueryable<ENROLLMENT> Query() => Items.AsQueryable();

        public void Add(ENROLLMENT enrollment) => Items.Add(enrollment);

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveCount++;
            return Task.CompletedTask;
        }
    }

    private sealed class NoSummaries : ICourseSummaryReader
    {
        public Task<IReadOnlyDictionary<Guid, CourseSummaryInfo>> GetCourseSummariesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, CourseSummaryInfo>>(new Dictionary<Guid, CourseSummaryInfo>());
    }

    private sealed class NoCatalog : ICatalogPriceContract
    {
        public Task<IReadOnlyDictionary<Guid, CoursePriceInfo>> GetPublishedCoursePricesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> IsEpisodeFreePreviewAsync(Guid episodeId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<Guid?> GetCourseIdForEpisodeAsync(Guid episodeId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> IsInstructorOwnerOfEpisodeAsync(Guid episodeId, Guid instructorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<bool> IsInstructorOwnerOfCourseAsync(Guid courseId, Guid instructorUserId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<int> GetPendingReviewsCountAsync(CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, string>> GetCourseTitlesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyDictionary<Guid, decimal>> GetInstructorRevenueSharePercentsAsync(IEnumerable<Guid> instructorIds, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private readonly InMemoryEnrollments _enrollments = new();
    private readonly RecordingCourseEnrollmentCountUpdater _updater = new();
    private readonly FixedClock _clock = new(Now);

    private EnrollmentService Service() => new(_enrollments, new EmptyCertificates(), _clock, new NoSummaries(), _updater);

    private LearningAccessContract Contract() => new(_enrollments, new NoCatalog(), _clock, _updater);

    private ENROLLMENT Existing(EnrollmentStatus status, DateTime? expiresAtUtc = null, Guid? courseId = null)
    {
        var enrollment = ENROLLMENT.Create(Guid.NewGuid(), courseId ?? Guid.NewGuid(), null, EnrollmentSource.Purchase, expiresAtUtc, _clock);
        switch (status)
        {
            case EnrollmentStatus.Expired:
                enrollment.Expire();
                break;
            case EnrollmentStatus.Revoked:
                enrollment.Revoke();
                break;
        }

        _enrollments.Items.Add(enrollment);
        return enrollment;
    }

    // ---- The rule itself ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(EnrollmentStatus.Active, true)]
    [InlineData(EnrollmentStatus.Expired, true)] // an access that merely lapsed still counts: they did learn here
    [InlineData(EnrollmentStatus.Revoked, false)]
    public void Counts_OnlyARevokedEnrollmentIsExcluded(EnrollmentStatus status, bool counts)
    {
        Assert.Equal(counts, EnrollmentCountRules.Counts(status));
    }

    [Theory]
    [InlineData(null, 1)] // a brand-new enrollment
    [InlineData(EnrollmentStatus.Revoked, 1)] // bought again after being revoked
    [InlineData(EnrollmentStatus.Expired, 0)] // already counted
    [InlineData(EnrollmentStatus.Active, 0)] // access past its date but the status never moved — already counted
    public void DeltaForGrant_OnlyAnEnrollmentThatDidNotCountStartsCounting(EnrollmentStatus? previous, int expected)
    {
        Assert.Equal(expected, EnrollmentCountRules.DeltaForGrant(previous));
    }

    [Theory]
    [InlineData(EnrollmentStatus.Active, -1)]
    [InlineData(EnrollmentStatus.Expired, -1)]
    [InlineData(EnrollmentStatus.Revoked, 0)] // revoking twice must not subtract twice
    public void DeltaForRevoke_OnlyAnEnrollmentThatCountedStopsCounting(EnrollmentStatus previous, int expected)
    {
        Assert.Equal(expected, EnrollmentCountRules.DeltaForRevoke(previous));
    }

    [Fact]
    public async Task ReportAsync_ZeroDelta_CostsNoCall_AndAnyOtherDeltaIsForwarded()
    {
        var courseId = Guid.NewGuid();

        await EnrollmentCountRules.ReportAsync(_updater, courseId, 0, CancellationToken.None);
        await EnrollmentCountRules.ReportAsync(_updater, courseId, 1, CancellationToken.None);
        await EnrollmentCountRules.ReportAsync(_updater, courseId, -1, CancellationToken.None);

        Assert.Equal([(courseId, 1), (courseId, -1)], _updater.Adjustments);
    }

    // ---- EnrollmentService (admin create / revoke) ---------------------------------------------------------

    [Fact]
    public async Task Service_Create_ANewEnrollment_CountsOne_AfterItIsSaved()
    {
        var courseId = Guid.NewGuid();

        var result = await Service().CreateAsync(new CreateEnrollmentCommand(Guid.NewGuid(), courseId, null, EnrollmentSource.Admin, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([(courseId, 1)], _updater.Adjustments);
        Assert.Equal(1, _enrollments.SaveCount); // saved first, reported second (Catalog owns the number; Learning reports the change)
    }

    [Fact]
    public async Task Service_Create_OverARevokedEnrollment_CountsOneAgain()
    {
        var revoked = Existing(EnrollmentStatus.Revoked);

        var result = await Service().CreateAsync(new CreateEnrollmentCommand(revoked.USER_ID, revoked.COURSE_ID, Guid.NewGuid(), EnrollmentSource.Purchase, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EnrollmentStatus.Active, revoked.STATUS);
        Assert.Equal([(revoked.COURSE_ID, 1)], _updater.Adjustments);
    }

    [Fact]
    public async Task Service_Create_OverAnExpiredEnrollment_ReactivatesWithoutCountingAgain()
    {
        var expired = Existing(EnrollmentStatus.Expired);

        var result = await Service().CreateAsync(new CreateEnrollmentCommand(expired.USER_ID, expired.COURSE_ID, Guid.NewGuid(), EnrollmentSource.Purchase, null), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EnrollmentStatus.Active, expired.STATUS);
        Assert.Empty(_updater.Adjustments); // it never stopped counting
    }

    [Fact]
    public async Task Service_Create_WhenAlreadyEnrolled_IsAConflict_AndCountsNothing()
    {
        var active = Existing(EnrollmentStatus.Active);

        var result = await Service().CreateAsync(new CreateEnrollmentCommand(active.USER_ID, active.COURSE_ID, null, EnrollmentSource.Admin, null), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(_updater.Adjustments);
    }

    [Theory]
    [InlineData(EnrollmentStatus.Active)]
    [InlineData(EnrollmentStatus.Expired)]
    public async Task Service_Revoke_AnEnrollmentThatCounted_SubtractsOne(EnrollmentStatus before)
    {
        var enrollment = Existing(before);

        var result = await Service().RevokeAsync(enrollment.ENROLLMENT_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EnrollmentStatus.Revoked, enrollment.STATUS);
        Assert.Equal([(enrollment.COURSE_ID, -1)], _updater.Adjustments);
    }

    [Fact]
    public async Task Service_Revoke_Twice_SubtractsOnlyOnce()
    {
        var enrollment = Existing(EnrollmentStatus.Active);
        var service = Service();

        await service.RevokeAsync(enrollment.ENROLLMENT_ID, CancellationToken.None);
        await service.RevokeAsync(enrollment.ENROLLMENT_ID, CancellationToken.None);

        Assert.Equal([(enrollment.COURSE_ID, -1)], _updater.Adjustments);
    }

    [Fact]
    public async Task Service_Revoke_UnknownEnrollment_CountsNothing()
    {
        var result = await Service().RevokeAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Empty(_updater.Adjustments);
    }

    // ---- LearningAccessContract.EnrollUserAsync (single course, e.g. a payment fulfilment) --------------------

    [Fact]
    public async Task Contract_EnrollUser_NewEnrollment_CountsOne()
    {
        var courseId = Guid.NewGuid();

        var result = await Contract().EnrollUserAsync(Guid.NewGuid(), courseId, Guid.NewGuid(), "Purchase", null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal([(courseId, 1)], _updater.Adjustments);
    }

    [Theory]
    [InlineData(EnrollmentStatus.Revoked, 1)]
    [InlineData(EnrollmentStatus.Expired, 0)]
    public async Task Contract_EnrollUser_ReactivatingAnOldEnrollment_CountsOnlyWhenItHadStoppedCounting(EnrollmentStatus before, int expectedDelta)
    {
        var existing = Existing(before);

        var result = await Contract().EnrollUserAsync(existing.USER_ID, existing.COURSE_ID, Guid.NewGuid(), "Purchase", null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(EnrollmentStatus.Active, existing.STATUS);
        if (expectedDelta == 0)
        {
            Assert.Empty(_updater.Adjustments);
        }
        else
        {
            Assert.Equal([(existing.COURSE_ID, expectedDelta)], _updater.Adjustments);
        }
    }

    [Fact]
    public async Task Contract_EnrollUser_AnActiveAccessPastItsDate_RenewsWithoutCountingAgain()
    {
        var lapsed = Existing(EnrollmentStatus.Active, expiresAtUtc: Now.AddDays(-1));

        await Contract().EnrollUserAsync(lapsed.USER_ID, lapsed.COURSE_ID, Guid.NewGuid(), "Purchase", Now.AddDays(30), CancellationToken.None);

        Assert.Empty(_updater.Adjustments);
    }

    [Fact]
    public async Task Contract_EnrollUser_ARetriedDelivery_IsAnIdempotentNoOp_AndNeverCountsTwice()
    {
        var courseId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var contract = Contract();

        await contract.EnrollUserAsync(userId, courseId, orderId, "Purchase", null, CancellationToken.None);
        await contract.EnrollUserAsync(userId, courseId, orderId, "Purchase", null, CancellationToken.None); // the webhook is delivered again

        Assert.Equal([(courseId, 1)], _updater.Adjustments);
    }

    // ---- LearningAccessContract.EnrollUserInCoursesAsync (batched purchase) -----------------------------------

    [Fact]
    public async Task Contract_EnrollUserInCourses_ReportsEachCourseOnce_InAscendingCourseIdOrder_AndSkipsWhatDidNotChange()
    {
        var userId = Guid.NewGuid();
        var brandNew = Guid.NewGuid();
        var revokedForThisUser = ENROLLMENT.Create(userId, Guid.NewGuid(), null, EnrollmentSource.Purchase, null, _clock);
        revokedForThisUser.Revoke();
        var expiredForThisUser = ENROLLMENT.Create(userId, Guid.NewGuid(), null, EnrollmentSource.Purchase, null, _clock);
        expiredForThisUser.Expire();
        var activeForThisUser = ENROLLMENT.Create(userId, Guid.NewGuid(), null, EnrollmentSource.Purchase, null, _clock);
        _enrollments.Items.AddRange([revokedForThisUser, expiredForThisUser, activeForThisUser]);

        var result = await Contract().EnrollUserInCoursesAsync(
            userId,
            "Purchase",
            [
                new CourseEnrollmentGrant(brandNew, Guid.NewGuid(), null),
                new CourseEnrollmentGrant(revokedForThisUser.COURSE_ID, Guid.NewGuid(), null),
                new CourseEnrollmentGrant(expiredForThisUser.COURSE_ID, Guid.NewGuid(), null),
                new CourseEnrollmentGrant(activeForThisUser.COURSE_ID, Guid.NewGuid(), null),
            ],
            CancellationToken.None);

        Assert.True(result.IsSuccess);

        // +1 for the brand-new and the previously revoked; nothing for the expired (already counted) or the still-active (an idempotent no-op).
        var expected = new[] { brandNew, revokedForThisUser.COURSE_ID }.OrderBy(id => id).Select(id => (id, 1)).ToList();
        Assert.Equal(expected, _updater.Adjustments);
        Assert.Equal(1, _enrollments.SaveCount);
    }

    [Fact]
    public async Task Contract_EnrollUserInCourses_ARetriedBatch_CountsNothingTheSecondTime()
    {
        var userId = Guid.NewGuid();
        var grants = new[] { new CourseEnrollmentGrant(Guid.NewGuid(), Guid.NewGuid(), null), new CourseEnrollmentGrant(Guid.NewGuid(), Guid.NewGuid(), null) };
        var contract = Contract();

        await contract.EnrollUserInCoursesAsync(userId, "Purchase", grants, CancellationToken.None);
        var firstRun = _updater.Adjustments.Count;
        await contract.EnrollUserInCoursesAsync(userId, "Purchase", grants, CancellationToken.None);

        Assert.Equal(2, firstRun);
        Assert.Equal(2, _updater.Adjustments.Count);
    }

    [Fact]
    public async Task Contract_EnrollUserInCourses_NoGrants_ReportsNothing()
    {
        var result = await Contract().EnrollUserInCoursesAsync(Guid.NewGuid(), "Purchase", [], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Empty(_updater.Adjustments);
    }

    private sealed class EmptyCertificates : ICertificateRepository
    {
        public Task<CERTIFICATE?> GetByIdAsync(Guid certificateId, CancellationToken cancellationToken) => Task.FromResult<CERTIFICATE?>(null);

        public Task<CERTIFICATE?> GetByEnrollmentIdAsync(Guid enrollmentId, CancellationToken cancellationToken) => Task.FromResult<CERTIFICATE?>(null);

        public Task<CERTIFICATE?> GetByVerifyCodeAsync(string verifyCode, CancellationToken cancellationToken) => Task.FromResult<CERTIFICATE?>(null);

        public IQueryable<CERTIFICATE> Query() => Array.Empty<CERTIFICATE>().AsQueryable();

        public void Add(CERTIFICATE certificate)
        {
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
