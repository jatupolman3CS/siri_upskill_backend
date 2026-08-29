using Siri.Modules.Analytics.Application;
using Siri.Modules.Analytics.Domain;
using Siri.Modules.Analytics.Features.InstructorAnalytics;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning.Contracts;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Analytics;

public sealed class InstructorAnalyticsTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private sealed class FakeCatalogPriceContract(List<Guid> ownedCourseIds, List<CourseEpisodeInfo> episodes) : ICatalogPriceContract
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
            var dict = courseIds.ToDictionary(id => id, id => $"COURSE {id}");
            return Task.FromResult<IReadOnlyDictionary<Guid, string>>(dict);
        }

        public Task<IReadOnlyDictionary<Guid, decimal>> GetInstructorRevenueSharePercentsAsync(IEnumerable<Guid> instructorIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>());

        public Task<IReadOnlyList<Guid>> GetCourseIdsByInstructorUserIdAsync(Guid instructorUserId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<Guid>>(ownedCourseIds);

        public Task<IReadOnlyList<CourseEpisodeInfo>> GetEpisodesForCoursesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CourseEpisodeInfo>>(episodes);
    }

    private sealed class FakeUserContactReader : IUserContactReader
    {
        public Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<string?>("test@example.com");

        public Task<(string? Email, string? DisplayName)> GetUserContactInfoAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<(string?, string?)>(("test@example.com", "Test Student"));

        public Task<IReadOnlyDictionary<Guid, (string Email, string DisplayName)>> GetUsersContactInfoAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
        {
            var dict = userIds.ToDictionary(id => id, id => ("test@example.com", $"Student {id}"));
            return Task.FromResult<IReadOnlyDictionary<Guid, (string Email, string DisplayName)>>(dict);
        }
    }

    private sealed class FakeLearningAnalyticsContract(List<StudentCourseProgressRecord> progressRecords) : ILearningAnalyticsContract
    {
        public Task<IReadOnlyList<EpisodeDropOffItem>> GetEpisodeDropOffRollupAsync(DateOnly date, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EpisodeDropOffItem>>([]);

        public Task<IReadOnlyList<DailyCourseActivityItem>> GetDailyCourseActivityAsync(DateOnly date, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DailyCourseActivityItem>>([]);

        public Task<int> PurgeOldWatchEventsAsync(DateTime olderThanUtc, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<StudentCourseProgressRecord>> GetStudentProgressByCoursesAsync(IEnumerable<Guid> courseIds, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<StudentCourseProgressRecord>>(progressRecords);
    }

    private sealed class FakeDailyCourseStatRepository(List<DAILY_COURSE_STAT> stats) : IDailyCourseStatRepository
    {
        public Task<DAILY_COURSE_STAT?> GetAsync(DateOnly date, Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult<DAILY_COURSE_STAT?>(null);

        public Task<IReadOnlyList<DAILY_COURSE_STAT>> GetForCourseAsync(Guid courseId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DAILY_COURSE_STAT>>(stats.Where(s => s.COURSE_ID == courseId).ToList());

        public Task<IReadOnlyList<CourseStatAggregate>> GetTopCoursesAsync(DateOnly fromDate, DateOnly toDate, int limit, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CourseStatAggregate>>([]);
    }

    private sealed class FakeEpisodeDropOffRepository(List<EPISODE_DROP_OFF> dropOffs) : IEpisodeDropOffRepository
    {
        public Task<EPISODE_DROP_OFF?> GetAsync(DateOnly date, Guid episodeId, CancellationToken cancellationToken) =>
            Task.FromResult<EPISODE_DROP_OFF?>(null);

        public Task<IReadOnlyList<EPISODE_DROP_OFF>> GetForEpisodeAsync(Guid episodeId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EPISODE_DROP_OFF>>(dropOffs.Where(d => d.EPISODE_ID == episodeId).ToList());
    }

    [Fact]
    public async Task HandleAsync_EmptyCourses_ReturnsZeroedResponse()
    {
        var instructorId = Guid.NewGuid();
        var handler = new GetInstructorAnalyticsHandler(
            new FakeCatalogPriceContract([], []),
            new FakeLearningAnalyticsContract([]),
            new FakeUserContactReader(),
            new FakeDailyCourseStatRepository([]),
            new FakeEpisodeDropOffRepository([]),
            new FakeClock(DateTime.UtcNow));

        var result = await handler.HandleAsync(instructorId, "30d", null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.TotalViews);
        Assert.Equal(0, result.Value.TotalEnrollments);
        Assert.Equal(0m, result.Value.TotalRevenue);
        Assert.Empty(result.Value.Students);
        Assert.Empty(result.Value.DropOffStats);
    }

    [Fact]
    public async Task HandleAsync_WithData_AggregatesCorrectly()
    {
        var instructorId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var stat = DAILY_COURSE_STAT.Create(today, courseId);
        stat.ApplyRollup(100, 5, 2500m, 75m);

        var dropOff = EPISODE_DROP_OFF.Create(today, episodeId);
        dropOff.ApplyRollup(100, 80, 80m);

        var progress = new StudentCourseProgressRecord(Guid.NewGuid(), Guid.NewGuid(), courseId, 80m, DateTime.UtcNow);

        var handler = new GetInstructorAnalyticsHandler(
            new FakeCatalogPriceContract([courseId], [new CourseEpisodeInfo(episodeId, courseId, "Intro", 1)]),
            new FakeLearningAnalyticsContract([progress]),
            new FakeUserContactReader(),
            new FakeDailyCourseStatRepository([stat]),
            new FakeEpisodeDropOffRepository([dropOff]),
            new FakeClock(DateTime.UtcNow));

        var result = await handler.HandleAsync(instructorId, "30d", null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(100, result.Value.TotalViews);
        Assert.Equal(5, result.Value.TotalEnrollments);
        Assert.Equal(2500m, result.Value.TotalRevenue);
        Assert.Equal(75m, result.Value.AvgCompletionRate);
        Assert.Single(result.Value.DropOffStats);
        Assert.Equal(80m, result.Value.DropOffStats[0].CompletionRate);
        Assert.Single(result.Value.Students);
    }
}
