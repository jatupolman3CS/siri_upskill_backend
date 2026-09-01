using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Learning.Domain;
using Siri.Modules.Learning.Infrastructure.Contracts;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Learning;

/// <summary>
/// Covers the N+1 fix on <see cref="ILearningAccessContract.EnrollUserInCoursesAsync"/> and
/// <see cref="ILearningAccessContract.HasActiveEnrollmentsAsync"/> — both used to only exist as
/// per-course loops over <see cref="ILearningAccessContract.EnrollUserAsync"/>/
/// <see cref="ILearningAccessContract.HasActiveEnrollmentAsync"/> from
/// <c>Siri.Modules.Commerce.Application.StripeWebhookHandler</c>/<c>OrderService</c>. These tests exercise
/// the real batched implementation (not the interface's per-course default fallback) directly against
/// <see cref="LearningAccessContract"/>, asserting both on call counts (proving one round trip instead of
/// N) and on outcome correctness (create vs. reactivate vs. idempotent no-op per course, same as the
/// single-course overload).
/// </summary>
public sealed class LearningAccessContractTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
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

        public Task<IReadOnlyDictionary<Guid, string>> GetCourseTitlesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());

        public Task<IReadOnlyDictionary<Guid, decimal>> GetInstructorRevenueSharePercentsAsync(IEnumerable<Guid> instructorIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>());
    }

    /// <summary>Tracks call counts on the batched vs. single-course lookups so tests can prove the
    /// contract's real implementation issues one round trip for a multi-course batch instead of looping.
    /// </summary>
    private sealed class FakeEnrollmentRepository : IEnrollmentRepository
    {
        public readonly Dictionary<Guid, ENROLLMENT> Enrollments = [];
        public int GetByUserAndCourseCallCount;
        public int GetByUserAndCoursesCallCount;
        public int SaveChangesCallCount;

        public Task<ENROLLMENT?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Enrollments.TryGetValue(id, out var enrollment) ? enrollment : null);

        public Task<ENROLLMENT?> GetByUserAndCourseAsync(Guid userId, Guid courseId, CancellationToken cancellationToken)
        {
            GetByUserAndCourseCallCount++;
            return Task.FromResult(Enrollments.Values.FirstOrDefault(e => e.USER_ID == userId && e.COURSE_ID == courseId));
        }

        public Task<IReadOnlyList<ENROLLMENT>> GetByUserAndCoursesAsync(Guid userId, IReadOnlyCollection<Guid> courseIds, CancellationToken cancellationToken)
        {
            GetByUserAndCoursesCallCount++;
            var result = Enrollments.Values.Where(e => e.USER_ID == userId && courseIds.Contains(e.COURSE_ID)).ToList();
            return Task.FromResult<IReadOnlyList<ENROLLMENT>>(result);
        }

        public IQueryable<ENROLLMENT> Query() => Enrollments.Values.AsQueryable();

        public void Add(ENROLLMENT enrollment) => Enrollments[enrollment.ENROLLMENT_ID] = enrollment;

        public Task SaveChangesAsync(CancellationToken cancellationToken)
        {
            SaveChangesCallCount++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task EnrollUserInCoursesAsync_NewCourses_CreatesOneEnrollmentPerCourseInOneBatch()
    {
        var repo = new FakeEnrollmentRepository();
        var contract = new LearningAccessContract(repo, new FakeCatalogPriceContract(), new FakeClock(new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc)));

        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var userId = Guid.NewGuid();
        var course1 = Guid.NewGuid();
        var course2 = Guid.NewGuid();
        var orderId = Guid.NewGuid();

        var grants = new List<CourseEnrollmentGrant>
        {
            new(course1, orderId, now.AddDays(30)),
            new(course2, orderId, null),
        };

        var result = await contract.EnrollUserInCoursesAsync(userId, "Purchase", grants, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, repo.Enrollments.Count);

        // Proves the batch: one lookup query and one SaveChangesAsync for both courses, not one of each
        // per course (which is what the old per-course loop over EnrollUserAsync did).
        Assert.Equal(1, repo.GetByUserAndCoursesCallCount);
        Assert.Equal(0, repo.GetByUserAndCourseCallCount);
        Assert.Equal(1, repo.SaveChangesCallCount);

        var enrollment1 = repo.Enrollments.Values.Single(e => e.COURSE_ID == course1);
        Assert.Equal(EnrollmentStatus.Active, enrollment1.STATUS);
        Assert.Equal(now.AddDays(30), enrollment1.EXPIRES_AT_UTC);
        Assert.Equal(orderId, enrollment1.ORDER_ID);
        Assert.Equal(EnrollmentSource.Purchase, enrollment1.SOURCE);

        var enrollment2 = repo.Enrollments.Values.Single(e => e.COURSE_ID == course2);
        Assert.Null(enrollment2.EXPIRES_AT_UTC);
    }

    [Fact]
    public async Task EnrollUserInCoursesAsync_MixOfNewAndExpiredCourses_CreatesAndReactivatesInOneBatch()
    {
        var repo = new FakeEnrollmentRepository();
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);
        var contract = new LearningAccessContract(repo, new FakeCatalogPriceContract(), clock);

        var userId = Guid.NewGuid();
        var expiredCourse = Guid.NewGuid();
        var newCourse = Guid.NewGuid();

        var existing = ENROLLMENT.Create(userId, expiredCourse, Guid.NewGuid(), EnrollmentSource.Purchase, now.AddDays(-10), clock);
        existing.UpdateProgress(42m, clock);
        existing.Expire();
        repo.Add(existing);

        var newOrderId = Guid.NewGuid();
        var grants = new List<CourseEnrollmentGrant>
        {
            new(expiredCourse, newOrderId, now.AddDays(30)),
            new(newCourse, newOrderId, null),
        };

        var result = await contract.EnrollUserInCoursesAsync(userId, "Purchase", grants, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, repo.Enrollments.Count); // no duplicate row created for the reactivated course
        Assert.Equal(1, repo.GetByUserAndCoursesCallCount);
        Assert.Equal(1, repo.SaveChangesCallCount);

        var reactivated = repo.Enrollments.Values.Single(e => e.COURSE_ID == expiredCourse);
        Assert.Equal(EnrollmentStatus.Active, reactivated.STATUS);
        Assert.Equal(newOrderId, reactivated.ORDER_ID);
        Assert.Equal(now.AddDays(30), reactivated.EXPIRES_AT_UTC);
        Assert.Equal(42m, reactivated.PROGRESS_PERCENT); // preserved by Reactivate — same as EnrollUserAsync

        Assert.Contains(repo.Enrollments.Values, e => e.COURSE_ID == newCourse && e.STATUS == EnrollmentStatus.Active);
    }

    [Fact]
    public async Task EnrollUserInCoursesAsync_CourseAlreadyActivelyEnrolled_IsIdempotentNoOpForThatCourseOnly()
    {
        var repo = new FakeEnrollmentRepository();
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);
        var contract = new LearningAccessContract(repo, new FakeCatalogPriceContract(), clock);

        var userId = Guid.NewGuid();
        var activeCourse = Guid.NewGuid();
        var newCourse = Guid.NewGuid();
        var originalOrderId = Guid.NewGuid();

        var existing = ENROLLMENT.Create(userId, activeCourse, originalOrderId, EnrollmentSource.Purchase, null, clock);
        repo.Add(existing);

        // Simulates a retried webhook delivery covering the already-enrolled course plus a genuinely new
        // one in the same order — the active course must be left untouched while the new one still gets
        // created, all in the same batch.
        var retriedOrderId = Guid.NewGuid();
        var grants = new List<CourseEnrollmentGrant>
        {
            new(activeCourse, retriedOrderId, now.AddDays(1)),
            new(newCourse, retriedOrderId, null),
        };

        var result = await contract.EnrollUserInCoursesAsync(userId, "Purchase", grants, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, repo.Enrollments.Count);

        var untouched = repo.Enrollments.Values.Single(e => e.COURSE_ID == activeCourse);
        Assert.Equal(originalOrderId, untouched.ORDER_ID); // not overwritten by the retried delivery
        Assert.Null(untouched.EXPIRES_AT_UTC); // not overwritten either

        Assert.Contains(repo.Enrollments.Values, e => e.COURSE_ID == newCourse);
    }

    [Fact]
    public async Task EnrollUserInCoursesAsync_EmptyGrantList_ReturnsSuccessWithoutAnyRepositoryCall()
    {
        var repo = new FakeEnrollmentRepository();
        var contract = new LearningAccessContract(repo, new FakeCatalogPriceContract(), new FakeClock(DateTime.UtcNow));

        var result = await contract.EnrollUserInCoursesAsync(Guid.NewGuid(), "Purchase", [], CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, repo.GetByUserAndCoursesCallCount);
        Assert.Equal(0, repo.SaveChangesCallCount);
    }

    [Fact]
    public async Task EnrollUserInCoursesAsync_EmptyUserId_ReturnsValidationFailure()
    {
        var repo = new FakeEnrollmentRepository();
        var contract = new LearningAccessContract(repo, new FakeCatalogPriceContract(), new FakeClock(DateTime.UtcNow));

        var result = await contract.EnrollUserInCoursesAsync(
            Guid.Empty, "Purchase", [new CourseEnrollmentGrant(Guid.NewGuid(), null, null)], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task EnrollUserInCoursesAsync_EmptyCourseIdInGrant_ReturnsValidationFailure()
    {
        var repo = new FakeEnrollmentRepository();
        var contract = new LearningAccessContract(repo, new FakeCatalogPriceContract(), new FakeClock(DateTime.UtcNow));

        var result = await contract.EnrollUserInCoursesAsync(
            Guid.NewGuid(), "Purchase", [new CourseEnrollmentGrant(Guid.Empty, null, null)], CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HasActiveEnrollmentsAsync_ReturnsOnlyActiveNonExpiredCourseIds_InOneQuery()
    {
        var repo = new FakeEnrollmentRepository();
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);
        var contract = new LearningAccessContract(repo, new FakeCatalogPriceContract(), clock);

        var userId = Guid.NewGuid();
        var activeCourse = Guid.NewGuid();
        var expiredCourse = Guid.NewGuid();
        var revokedCourse = Guid.NewGuid();
        var notEnrolledCourse = Guid.NewGuid();

        repo.Add(ENROLLMENT.Create(userId, activeCourse, null, EnrollmentSource.Purchase, now.AddDays(10), clock));
        repo.Add(ENROLLMENT.Create(userId, expiredCourse, null, EnrollmentSource.Purchase, now.AddDays(-1), clock));

        var revoked = ENROLLMENT.Create(userId, revokedCourse, null, EnrollmentSource.Purchase, null, clock);
        revoked.Revoke();
        repo.Add(revoked);

        var result = await contract.HasActiveEnrollmentsAsync(
            userId, [activeCourse, expiredCourse, revokedCourse, notEnrolledCourse], CancellationToken.None);

        Assert.Single(result);
        Assert.Contains(activeCourse, result);
        Assert.Equal(1, repo.GetByUserAndCoursesCallCount);
        Assert.Equal(0, repo.GetByUserAndCourseCallCount);
    }

    [Fact]
    public async Task HasActiveEnrollmentsAsync_LifetimeAccessCourse_CountsAsActive()
    {
        var repo = new FakeEnrollmentRepository();
        var now = new DateTime(2026, 8, 25, 12, 0, 0, DateTimeKind.Utc);
        var clock = new FakeClock(now);
        var contract = new LearningAccessContract(repo, new FakeCatalogPriceContract(), clock);

        var userId = Guid.NewGuid();
        var lifetimeCourse = Guid.NewGuid();
        repo.Add(ENROLLMENT.Create(userId, lifetimeCourse, null, EnrollmentSource.Purchase, null, clock));

        var result = await contract.HasActiveEnrollmentsAsync(userId, [lifetimeCourse], CancellationToken.None);

        Assert.Contains(lifetimeCourse, result);
    }

    [Fact]
    public async Task HasActiveEnrollmentsAsync_EmptyCourseIdList_ReturnsEmptyWithoutAnyRepositoryCall()
    {
        var repo = new FakeEnrollmentRepository();
        var contract = new LearningAccessContract(repo, new FakeCatalogPriceContract(), new FakeClock(DateTime.UtcNow));

        var result = await contract.HasActiveEnrollmentsAsync(Guid.NewGuid(), [], CancellationToken.None);

        Assert.Empty(result);
        Assert.Equal(0, repo.GetByUserAndCoursesCallCount);
    }
}
