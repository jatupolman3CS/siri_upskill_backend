using Siri.Modules.Analytics.Application;
using Siri.Modules.Analytics.Domain;
using Siri.Modules.Analytics.Features.AdminDashboardSummary;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Commerce.Contracts;
using Siri.Modules.Identity.Contracts;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Analytics;

public sealed class AdminDashboardSummaryTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private sealed class FakeIdentityStatsContract : IIdentityStatsContract
    {
        public Task<LearnerStats> GetLearnerStatsAsync(DateTime todayUtc, CancellationToken cancellationToken) =>
            Task.FromResult(new LearnerStats(15, 250));
    }

    private sealed class FakeCatalogPriceContract : ICatalogPriceContract
    {
        public Task<IReadOnlyDictionary<Guid, CoursePriceInfo>> GetPublishedCoursePricesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, CoursePriceInfo>>(new Dictionary<Guid, CoursePriceInfo>());

        public Task<bool> IsEpisodeFreePreviewAsync(Guid episodeId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<Guid?> GetCourseIdForEpisodeAsync(Guid episodeId, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);

        public Task<bool> IsInstructorOwnerOfEpisodeAsync(Guid episodeId, Guid instructorUserId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> IsInstructorOwnerOfCourseAsync(Guid courseId, Guid instructorUserId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<int> GetPendingReviewsCountAsync(CancellationToken cancellationToken) => Task.FromResult(4);

        public Task<IReadOnlyDictionary<Guid, string>> GetCourseTitlesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken)
        {
            var dict = courseIds.ToDictionary(id => id, id => $"COURSE {id}");
            return Task.FromResult<IReadOnlyDictionary<Guid, string>>(dict);
        }

        public Task<IReadOnlyDictionary<Guid, decimal>> GetInstructorRevenueSharePercentsAsync(IEnumerable<Guid> instructorIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>());
    }


    private sealed class FakeCommerceStatsContract : ICommerceStatsContract
    {
        public Task<CommerceDashboardStats> GetCommerceDashboardStatsAsync(DateTime todayUtc, CancellationToken cancellationToken) =>
            Task.FromResult(new CommerceDashboardStats(8500m, 2));

        public Task<IReadOnlyDictionary<Guid, decimal>> GetDailyCourseRevenueAsync(DateOnly date, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>());
    }

    private sealed class FakeDailyCourseStatRepository : IDailyCourseStatRepository
    {
        public Task<DAILY_COURSE_STAT?> GetAsync(DateOnly date, Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult<DAILY_COURSE_STAT?>(null);

        public Task<IReadOnlyList<DAILY_COURSE_STAT>> GetForCourseAsync(Guid courseId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DAILY_COURSE_STAT>>([]);

        public Task<IReadOnlyList<CourseStatAggregate>> GetTopCoursesAsync(DateOnly fromDate, DateOnly toDate, int limit, CancellationToken cancellationToken)
        {
            var course1 = Guid.NewGuid();
            var course2 = Guid.NewGuid();
            IReadOnlyList<CourseStatAggregate> list =
            [
                new CourseStatAggregate(course1, 50000m, 25, 400),
                new CourseStatAggregate(course2, 30000m, 15, 250),
            ];
            return Task.FromResult(list);
        }
    }

    [Fact]
    public async Task HandleAsync_AggregatesAllModulesCorrectly()
    {
        var identity = new FakeIdentityStatsContract();
        var catalog = new FakeCatalogPriceContract();
        var commerce = new FakeCommerceStatsContract();
        var analyticsRepo = new FakeDailyCourseStatRepository();
        var clock = new FakeClock(DateTime.UtcNow);

        var handler = new GetAdminDashboardSummaryHandler(identity, catalog, commerce, analyticsRepo, clock);
        var result = await handler.HandleAsync(CancellationToken.None);

        Assert.Equal(8500m, result.TodaySales);
        Assert.Equal(15, result.TodayNewLearners);
        Assert.Equal(250, result.TotalLearners);
        Assert.Equal(4, result.PendingCourseReviewsCount);
        Assert.Equal(2, result.PendingRefundsCount);
        Assert.Equal(2, result.TopCourses30Days.Count);
        Assert.Equal(50000m, result.TopCourses30Days[0].TotalRevenue);
        Assert.Equal(25, result.TopCourses30Days[0].TotalEnrollments);
    }
}
