using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Learning.Domain;
using Siri.Modules.Learning.Infrastructure.Contracts;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Learning;

/// <summary>
/// <see cref="ILearningAccessContract.GetActiveEnrolledCourseIdsAsync"/> (P11-05 section 3.2): the courses a user holds an active, unexpired enrollment for — the exact rule of
/// <see cref="ILearningAccessContract.HasActiveEnrollmentAsync"/>, so "my upcoming live classes" can never list a course the join gate would refuse.
/// </summary>
public sealed class ActiveEnrolledCourseIdsTests
{
    private static readonly DateTime Now = new(2026, 10, 7, 3, 0, 0, DateTimeKind.Utc);

    private readonly FakeClock _clock = new(Now);
    private readonly FakeEnrollments _repo = new();
    private readonly Guid _user = Guid.NewGuid();

    private LearningAccessContract Contract() => new(_repo, new NoCatalog(), _clock, new RecordingCourseEnrollmentCountUpdater());

    private ENROLLMENT Enroll(Guid? userId = null, DateTime? expiresAt = null, Action<ENROLLMENT>? shape = null)
    {
        var enrollment = ENROLLMENT.Create(userId ?? _user, Guid.NewGuid(), Guid.NewGuid(), EnrollmentSource.Purchase, expiresAt, _clock);
        shape?.Invoke(enrollment);
        _repo.Items.Add(enrollment);
        return enrollment;
    }

    [Fact]
    public async Task ReturnsOnlyActiveUnexpiredEnrollmentsOfThatUser_NotExpiredRevokedOrLapsedOnes()
    {
        var lifetime = Enroll(expiresAt: null);
        var future = Enroll(expiresAt: Now.AddDays(10));
        Enroll(expiresAt: Now); // expires exactly now: not active (the rule is strictly "expires after now")
        Enroll(expiresAt: Now.AddSeconds(-1)); // lapsed by date although the status was never flipped
        Enroll(shape: e => e.Expire());
        Enroll(shape: e => e.Revoke());
        Enroll(userId: Guid.NewGuid()); // someone else's

        var ids = await Contract().GetActiveEnrolledCourseIdsAsync(_user, CancellationToken.None);

        Assert.Equal(new[] { lifetime.COURSE_ID, future.COURSE_ID }.Order(), ids.Order());
    }

    [Fact]
    public async Task ARepurchasedEnrollment_IsActiveAgain()
    {
        var reactivated = Enroll(shape: e =>
        {
            e.Revoke();
            e.Reactivate(Guid.NewGuid(), Now.AddDays(30), _clock);
        });

        var ids = await Contract().GetActiveEnrolledCourseIdsAsync(_user, CancellationToken.None);

        Assert.Equal(reactivated.COURSE_ID, Assert.Single(ids));
    }

    [Fact]
    public async Task AgreesWithHasActiveEnrollment_ForEveryEnrollmentShape()
    {
        var all = new[]
        {
            Enroll(expiresAt: null),
            Enroll(expiresAt: Now.AddDays(1)),
            Enroll(expiresAt: Now),
            Enroll(expiresAt: Now.AddDays(-1)),
            Enroll(shape: e => e.Expire()),
            Enroll(shape: e => e.Revoke()),
        };
        var contract = Contract();

        var ids = (await contract.GetActiveEnrolledCourseIdsAsync(_user, CancellationToken.None)).ToHashSet();

        foreach (var enrollment in all)
        {
            Assert.Equal(
                await contract.HasActiveEnrollmentAsync(_user, enrollment.COURSE_ID, CancellationToken.None),
                ids.Contains(enrollment.COURSE_ID));
        }
    }

    [Fact]
    public async Task TheTimeComesFromTheClock()
    {
        var enrollment = Enroll(expiresAt: Now.AddHours(1));

        Assert.Contains(enrollment.COURSE_ID, await Contract().GetActiveEnrolledCourseIdsAsync(_user, CancellationToken.None));

        _clock.UtcNow = Now.AddHours(1).AddTicks(1);
        Assert.Empty(await Contract().GetActiveEnrolledCourseIdsAsync(_user, CancellationToken.None));
    }

    [Fact]
    public async Task AUserWithNoEnrollmentsOrAnEmptyId_GetsAnEmptyList()
    {
        Enroll(userId: Guid.NewGuid());

        Assert.Empty(await Contract().GetActiveEnrolledCourseIdsAsync(_user, CancellationToken.None));
        Assert.Empty(await Contract().GetActiveEnrolledCourseIdsAsync(Guid.Empty, CancellationToken.None));
    }

    [Fact]
    public async Task TheInterfaceDefault_ReportsNothing_SoOtherImplementersKeepCompiling()
    {
        ILearningAccessContract defaulted = new DefaultOnly();

        Assert.Empty(await defaulted.GetActiveEnrolledCourseIdsAsync(_user, CancellationToken.None));
    }

    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow { get; set; } = now;
    }

    /// <summary>Only <see cref="Query"/> matters; EF-style async operators work on it through <see cref="AsyncQueryable"/>.</summary>
    private sealed class FakeEnrollments : IEnrollmentRepository
    {
        public List<ENROLLMENT> Items { get; } = [];

        public Task<ENROLLMENT?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(e => e.ENROLLMENT_ID == id));

        public Task<ENROLLMENT?> GetByUserAndCourseAsync(Guid userId, Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(e => e.USER_ID == userId && e.COURSE_ID == courseId));

        public IQueryable<ENROLLMENT> Query() => AsyncQueryable.From(Items);

        public void Add(ENROLLMENT enrollment) => Items.Add(enrollment);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
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

        public Task<IReadOnlyDictionary<Guid, decimal>> GetInstructorRevenueSharePercentsAsync(IEnumerable<Guid> instructorIds, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    /// <summary>An implementer that never heard of the new member: it must still compile and report "nothing".</summary>
    private sealed class DefaultOnly : ILearningAccessContract
    {
        public Task<bool> CanUserAccessEpisodeAsync(Guid userId, Guid episodeId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> HasActiveEnrollmentAsync(Guid userId, Guid courseId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<Result> EnrollUserAsync(Guid userId, Guid courseId, Guid? orderId, string source, DateTime? expiresAtUtc, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success());
    }
}
