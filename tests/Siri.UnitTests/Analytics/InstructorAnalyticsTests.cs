using System.Text.Json;
using Siri.Modules.Analytics.Application;
using Siri.Modules.Analytics.Domain;
using Siri.Modules.Analytics.Features.InstructorAnalytics;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Live.Contracts;
using Siri.Modules.Payout.Contracts;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Analytics;

/// <summary>
/// The instructor dashboard/analytics handler (docs/contracts/P11-10-instructor-dashboard-summary.md §5): every number comes from a contract, nothing is estimated,
/// nothing invented, and one instructor can never see another's courses, sessions or revenue. All cross-module contracts are faked — the multi-tenant fakes hold
/// several instructors' data and answer only for the user id they are asked about, which is exactly what the real readers do.
/// </summary>
public sealed class InstructorAnalyticsTests
{
    private static readonly DateTime Now = new(2026, 10, 15, 13, 30, 0, DateTimeKind.Utc);

    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    /// <param name="ownedCourseIds">Returned for every user (single-tenant tests).</param>
    /// <param name="ownedByUser">When given, only the ids of the asked user are returned (multi-tenant tests).</param>
    /// <param name="resolveTitles">When false no course title can be resolved.</param>
    private sealed class FakeCatalogPriceContract(
        List<Guid> ownedCourseIds,
        List<CourseEpisodeInfo> episodes,
        bool resolveTitles = true,
        Dictionary<Guid, List<Guid>>? ownedByUser = null) : ICatalogPriceContract
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
            var dict = resolveTitles
                ? courseIds.ToDictionary(id => id, id => $"COURSE {id}")
                : new Dictionary<Guid, string>();
            return Task.FromResult<IReadOnlyDictionary<Guid, string>>(dict);
        }

        public Task<IReadOnlyDictionary<Guid, decimal>> GetInstructorRevenueSharePercentsAsync(IEnumerable<Guid> instructorIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>());

        public Task<IReadOnlyList<Guid>> GetCourseIdsByInstructorUserIdAsync(Guid instructorUserId, CancellationToken cancellationToken)
        {
            IReadOnlyList<Guid> ids = ownedByUser is null
                ? ownedCourseIds
                : ownedByUser.TryGetValue(instructorUserId, out var owned) ? owned : [];
            return Task.FromResult(ids);
        }

        public Task<IReadOnlyList<CourseEpisodeInfo>> GetEpisodesForCoursesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken)
        {
            var wanted = courseIds.ToHashSet();
            return Task.FromResult<IReadOnlyList<CourseEpisodeInfo>>(episodes.Where(e => wanted.Contains(e.CourseId)).ToList());
        }
    }

    /// <summary>Answers per user id, exactly as the real reader does — an unknown user has no profile and no courses.</summary>
    private sealed class FakeInstructorCourseStatsReader(Dictionary<Guid, InstructorCourseStatsInfo>? byUser = null, InstructorCourseStatsInfo? forAnyone = null) : IInstructorCourseStatsReader
    {
        public Task<InstructorCourseStatsInfo> GetByInstructorUserIdAsync(Guid instructorUserId, CancellationToken cancellationToken)
        {
            if (byUser is not null && byUser.TryGetValue(instructorUserId, out var info))
            {
                return Task.FromResult(info);
            }

            return Task.FromResult(forAnyone ?? new InstructorCourseStatsInfo(null, []));
        }
    }

    private sealed class FakeInstructorRevenueReader(Dictionary<(Guid ProfileId, string PeriodKey), decimal>? amounts = null) : IInstructorRevenueReader
    {
        public List<(Guid ProfileId, string PeriodKey)> Calls { get; } = [];

        public Task<decimal> GetNetRevenueForPeriodAsync(Guid instructorProfileId, string periodKey, CancellationToken cancellationToken)
        {
            Calls.Add((instructorProfileId, periodKey));
            return Task.FromResult(amounts is not null && amounts.TryGetValue((instructorProfileId, periodKey), out var amount) ? amount : 0m);
        }
    }

    /// <param name="resolveUsers">When false no learner account can be resolved.</param>
    private sealed class FakeUserContactReader(bool resolveUsers = true) : IUserContactReader
    {
        public Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<string?>("test@example.com");

        public Task<(string? Email, string? DisplayName)> GetUserContactInfoAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<(string?, string?)>(("test@example.com", "Test Student"));

        public Task<IReadOnlyDictionary<Guid, (string Email, string DisplayName)>> GetUsersContactInfoAsync(IEnumerable<Guid> userIds, CancellationToken cancellationToken)
        {
            var dict = resolveUsers
                ? userIds.ToDictionary(id => id, id => ("test@example.com", $"Student {id}"))
                : new Dictionary<Guid, (string Email, string DisplayName)>();
            return Task.FromResult<IReadOnlyDictionary<Guid, (string Email, string DisplayName)>>(dict);
        }
    }

    private sealed class FakeLearningAnalyticsContract(
        List<StudentCourseProgressRecord> progressRecords,
        long watchedSeconds = 0,
        LearnerCounts? learnerCounts = null) : ILearningAnalyticsContract
    {
        public DateTime? RequestedWatchedSinceUtc { get; private set; }

        public IReadOnlyCollection<Guid>? RequestedWatchedCourseIds { get; private set; }

        public IReadOnlyCollection<Guid>? RequestedLearnerCourseIds { get; private set; }

        public DateTime? RequestedLearnerSinceUtc { get; private set; }

        public Task<IReadOnlyList<EpisodeDropOffItem>> GetEpisodeDropOffRollupAsync(DateOnly date, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<EpisodeDropOffItem>>([]);

        public Task<IReadOnlyList<DailyCourseActivityItem>> GetDailyCourseActivityAsync(DateOnly date, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<DailyCourseActivityItem>>([]);

        public Task<int> PurgeOldWatchEventsAsync(DateTime olderThanUtc, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public Task<IReadOnlyList<StudentCourseProgressRecord>> GetStudentProgressByCoursesAsync(IEnumerable<Guid> courseIds, int limit, CancellationToken cancellationToken)
        {
            var wanted = courseIds.ToHashSet();
            return Task.FromResult<IReadOnlyList<StudentCourseProgressRecord>>(progressRecords.Where(p => wanted.Contains(p.CourseId)).ToList());
        }

        public Task<IReadOnlyList<EpisodeWatchTimeBucket>> GetEpisodeWatchTimeBucketsAsync(IEnumerable<Guid> courseIds, DateTime activeSinceUtc, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("The dashboard must use GetWatchedSecondsAsync (P11-10 §4), not the per-episode buckets.");

        public Task<long> GetWatchedSecondsAsync(IReadOnlyCollection<Guid> courseIds, DateTime sinceUtc, CancellationToken cancellationToken)
        {
            RequestedWatchedCourseIds = courseIds;
            RequestedWatchedSinceUtc = sinceUtc;
            return Task.FromResult(watchedSeconds);
        }

        public Task<LearnerCounts> GetLearnerCountsAsync(IReadOnlyCollection<Guid> courseIds, DateTime sinceUtc, CancellationToken cancellationToken)
        {
            RequestedLearnerCourseIds = courseIds;
            RequestedLearnerSinceUtc = sinceUtc;
            return Task.FromResult(learnerCounts ?? new LearnerCounts(0, 0));
        }
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

    /// <summary>Holds sessions of several instructors and answers instructor queries with the real reader's overlap semantics (<c>EndsAtUtc &gt; from</c>, <c>StartsAtUtc &lt; to</c>).</summary>
    private sealed class FakeLiveScheduleReader(IEnumerable<LiveSessionContext> sessions) : ILiveScheduleReader
    {
        private readonly List<LiveSessionContext> _sessions = sessions.ToList();

        public List<(Guid UserId, DateTime? From, DateTime? To, bool IncludeCancelled, bool NewestFirst, int Skip, int Take)> InstructorCalls { get; } = [];

        public Task<IReadOnlyList<LiveSessionInfo>> GetSessionsForCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LiveSessionInfo>>([]);

        public Task<IReadOnlyList<LiveSessionInfo>> GetUpcomingSessionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<LiveSessionInfo>>([]);

        public Task<LiveSessionInfo?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
            Task.FromResult<LiveSessionInfo?>(null);

        public Task<LiveSessionContextPage> GetInstructorSessionContextsAsync(
            Guid instructorUserId,
            DateTime? fromUtc,
            DateTime? toUtc,
            bool includeCancelled,
            bool newestFirst,
            int skip,
            int take,
            CancellationToken cancellationToken)
        {
            InstructorCalls.Add((instructorUserId, fromUtc, toUtc, includeCancelled, newestFirst, skip, take));

            var filtered = _sessions
                .Where(s => s.InstructorUserId == instructorUserId)
                .Where(s => fromUtc == null || s.EndsAtUtc > fromUtc)
                .Where(s => toUtc == null || s.StartsAtUtc < toUtc)
                .Where(s => includeCancelled || s.Status == LiveSessionStatus.Scheduled)
                .ToList();

            var ordered = newestFirst
                ? filtered.OrderByDescending(s => s.StartsAtUtc).ToList()
                : filtered.OrderBy(s => s.StartsAtUtc).ToList();

            return Task.FromResult(new LiveSessionContextPage(ordered.Skip(skip).Take(take).ToList(), filtered.Count));
        }
    }

    private sealed class FakeLiveAttendanceReader(Dictionary<Guid, LiveSessionStats>? stats = null) : ILiveAttendanceReader
    {
        public List<IReadOnlyCollection<Guid>> StatsCalls { get; } = [];

        public Task<IReadOnlySet<Guid>> GetCourseIdsAttendedAsync(Guid userId, IReadOnlyCollection<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());

        public Task<IReadOnlyDictionary<Guid, LiveSessionStats>> GetSessionStatsAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken)
        {
            StatsCalls.Add(sessionIds);

            // Like the real reader: every requested id is present (zeros when nobody joined / was invited).
            var result = sessionIds.ToDictionary(
                id => id,
                id => stats is not null && stats.TryGetValue(id, out var known) ? known : new LiveSessionStats(id, 0, 0, false));
            return Task.FromResult<IReadOnlyDictionary<Guid, LiveSessionStats>>(result);
        }
    }

    /// <summary>Everything a handler needs; defaults are empty single-tenant fakes so a test names only what it cares about.</summary>
    private sealed class Harness
    {
        public FakeCatalogPriceContract Catalog { get; init; } = new([], []);

        public FakeInstructorCourseStatsReader CourseStats { get; init; } = new();

        public FakeInstructorRevenueReader Revenue { get; init; } = new();

        public FakeLearningAnalyticsContract Learning { get; init; } = new([]);

        public FakeLiveScheduleReader Schedule { get; init; } = new([]);

        public FakeLiveAttendanceReader Attendance { get; init; } = new();

        public FakeUserContactReader Users { get; init; } = new();

        public List<DAILY_COURSE_STAT> Stats { get; init; } = [];

        public List<EPISODE_DROP_OFF> DropOffs { get; init; } = [];

        public DateTime ClockNow { get; init; } = Now;

        public GetInstructorAnalyticsHandler Build() =>
            new(
                Catalog,
                CourseStats,
                Revenue,
                Learning,
                Schedule,
                Attendance,
                Users,
                new FakeDailyCourseStatRepository(Stats),
                new FakeEpisodeDropOffRepository(DropOffs),
                new FakeClock(ClockNow));
    }

    /// <summary>A catalog where the instructor owns one course — live sessions belong to courses, so the live section is only built for an instructor who has some.</summary>
    private static FakeCatalogPriceContract OwnsACourse() => new([Guid.NewGuid()], []);

    private static InstructorCourseStat Stat(
        string status = "Published",
        decimal ratingAverage = 0m,
        int ratingCount = 0,
        string format = "OnDemand",
        string? title = null,
        Guid? id = null) =>
        new(id ?? Guid.NewGuid(), title ?? "Course", "course-slug", status, format, 1200m, 7, ratingAverage, ratingCount, null);

    private static InstructorCourseStatsInfo Profile(Guid profileId, params InstructorCourseStat[] courses) => new(profileId, courses);

    private static LiveSessionContext Session(
        Guid instructorUserId,
        DateTime startsAtUtc,
        TimeSpan? length = null,
        LiveSessionStatus status = LiveSessionStatus.Scheduled,
        Guid? courseId = null,
        string title = "คาบทดสอบ",
        Guid? id = null) =>
        new(
            id ?? Guid.NewGuid(),
            courseId ?? Guid.NewGuid(),
            "คอร์สสด",
            "live-course",
            title,
            null,
            startsAtUtc,
            startsAtUtc + (length ?? TimeSpan.FromHours(2)),
            status,
            null,
            null,
            Guid.NewGuid(),
            instructorUserId,
            "Instructor",
            false);

    // ---- Existing behaviour that must not change ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_EmptyCourses_ReturnsZeroedResponse()
    {
        var handler = new Harness().Build();

        var result = await handler.HandleAsync(Guid.NewGuid(), "30d", null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(0, result.Value.TotalViews);
        Assert.Equal(0, result.Value.TotalEnrollments);
        Assert.Equal(0m, result.Value.TotalRevenue);
        Assert.Equal(0, result.Value.TotalWatchMinutes);
        Assert.Empty(result.Value.Students);
        Assert.Empty(result.Value.DropOffStats);
        Assert.Empty(result.Value.Courses);
    }

    [Fact]
    public async Task HandleAsync_EmptyUserId_IsForbidden()
    {
        var result = await new Harness().Build().HandleAsync(Guid.Empty, "30d", null, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task HandleAsync_WithData_AggregatesCorrectly()
    {
        var courseId = Guid.NewGuid();
        var episodeId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(Now);

        var stat = DAILY_COURSE_STAT.Create(today, courseId);
        stat.ApplyRollup(100, 5, 2500m, 75m);

        var dropOff = EPISODE_DROP_OFF.Create(today, episodeId);
        dropOff.ApplyRollup(100, 80, 80m);

        var progress = new StudentCourseProgressRecord(Guid.NewGuid(), Guid.NewGuid(), courseId, 80m, Now, 1);

        var handler = new Harness
        {
            Catalog = new FakeCatalogPriceContract([courseId], [new CourseEpisodeInfo(episodeId, courseId, "Intro", 1)]),
            Learning = new FakeLearningAnalyticsContract([progress]),
            Stats = [stat],
            DropOffs = [dropOff],
        }.Build();

        var result = await handler.HandleAsync(Guid.NewGuid(), "30d", null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(100, result.Value.TotalViews);
        Assert.Equal(5, result.Value.TotalEnrollments);
        Assert.Equal(2500m, result.Value.TotalRevenue);
        Assert.Equal(75m, result.Value.AvgCompletionRate);
        Assert.Single(result.Value.DropOffStats);
        Assert.Equal(80m, result.Value.DropOffStats[0].CompletionRate);
        Assert.Single(result.Value.Students);
    }

    // ---- TotalWatchMinutes: the contract's recorded seconds, never a views multiplier ------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_TotalWatchMinutes_IsTheRecordedSecondsFromTheContract_NotViewsTimesSix()
    {
        var courseId = Guid.NewGuid();
        var today = DateOnly.FromDateTime(Now);

        // 100 views — the old estimate would have reported 100 * 6 = 600 minutes.
        var stat = DAILY_COURSE_STAT.Create(today, courseId);
        stat.ApplyRollup(100, 5, 2500m, 75m);

        // 1920 recorded seconds => 32 minutes.
        var handler = new Harness
        {
            Catalog = new FakeCatalogPriceContract([courseId], []),
            Learning = new FakeLearningAnalyticsContract([], watchedSeconds: 1920),
            Stats = [stat],
        }.Build();

        var result = await handler.HandleAsync(Guid.NewGuid(), "30d", null, CancellationToken.None);

        Assert.Equal(100, result.Value.TotalViews);
        Assert.Equal(32, result.Value.TotalWatchMinutes);
        Assert.NotEqual(600, result.Value.TotalWatchMinutes);
    }

    [Theory]
    [InlineData(0L, 0)]
    [InlineData(59L, 0)]
    [InlineData(60L, 1)]
    [InlineData(3599L, 59)]
    [InlineData(-5L, 0)]
    public async Task HandleAsync_TotalWatchMinutes_IsWholeMinutesTruncatedAndNeverNegative(long seconds, int expectedMinutes)
    {
        var courseId = Guid.NewGuid();
        var handler = new Harness
        {
            Catalog = new FakeCatalogPriceContract([courseId], []),
            Learning = new FakeLearningAnalyticsContract([], watchedSeconds: seconds),
        }.Build();

        var result = await handler.HandleAsync(Guid.NewGuid(), "30d", null, CancellationToken.None);

        Assert.Equal(expectedMinutes, result.Value.TotalWatchMinutes);
    }

    [Fact]
    public async Task HandleAsync_TotalWatchMinutes_NobodyWatched_IsGenuinelyZeroEvenWithViews()
    {
        var courseId = Guid.NewGuid();
        var stat = DAILY_COURSE_STAT.Create(DateOnly.FromDateTime(Now), courseId);
        stat.ApplyRollup(100, 5, 2500m, 75m);

        var handler = new Harness { Catalog = new FakeCatalogPriceContract([courseId], []), Stats = [stat] }.Build();

        var result = await handler.HandleAsync(Guid.NewGuid(), "30d", null, CancellationToken.None);

        Assert.Equal(100, result.Value.TotalViews);
        Assert.Equal(0, result.Value.TotalWatchMinutes);
    }

    [Theory]
    [InlineData("7d", 7)]
    [InlineData("30d", 30)]
    [InlineData("90d", 90)]
    [InlineData(null, 30)]
    public async Task HandleAsync_WatchTimeWindow_StartsAtTheSelectedRangeStart(string? range, int days)
    {
        var courseId = Guid.NewGuid();
        var learning = new FakeLearningAnalyticsContract([]);
        var handler = new Harness { Catalog = new FakeCatalogPriceContract([courseId], []), Learning = learning }.Build();

        await handler.HandleAsync(Guid.NewGuid(), range, null, CancellationToken.None);

        var expected = new DateTime(2026, 10, 15, 0, 0, 0, DateTimeKind.Utc).AddDays(-days);
        Assert.Equal(expected, learning.RequestedWatchedSinceUtc);
    }

    // ---- Students / course options: real values or null, never invented text ---------------------------------------------------------------------

    [Fact]
    public async Task HandleAsync_UnresolvableLearner_ReportsNullNameAndEmailInsteadOfInventedOnes()
    {
        var courseId = Guid.NewGuid();
        var progress = new StudentCourseProgressRecord(Guid.NewGuid(), Guid.NewGuid(), courseId, 40m, null, 0);

        var handler = new Harness
        {
            Catalog = new FakeCatalogPriceContract([courseId], []),
            Learning = new FakeLearningAnalyticsContract([progress]),
            Users = new FakeUserContactReader(resolveUsers: false),
        }.Build();

        var result = await handler.HandleAsync(Guid.NewGuid(), "30d", null, CancellationToken.None);

        var student = Assert.Single(result.Value.Students);
        Assert.Null(student.Name);
        Assert.Null(student.Email);
        Assert.Equal($"COURSE {courseId}", student.CourseTitle);
        Assert.Null(student.LastActiveAtUtc);
    }

    [Fact]
    public async Task HandleAsync_StudentWhoseCourseCannotBeResolved_IsLeftOutNotShownUnderAnInventedTitle()
    {
        var courseId = Guid.NewGuid();
        var progress = new StudentCourseProgressRecord(Guid.NewGuid(), Guid.NewGuid(), courseId, 40m, null, 0);

        var handler = new Harness
        {
            Catalog = new FakeCatalogPriceContract([courseId], [], resolveTitles: false),
            Learning = new FakeLearningAnalyticsContract([progress]),
        }.Build();

        var result = await handler.HandleAsync(Guid.NewGuid(), "30d", null, CancellationToken.None);

        Assert.Empty(result.Value.Students);
        Assert.Empty(result.Value.Courses);
    }

    [Fact]
    public async Task HandleAsync_NothingResolvable_ResponseCarriesNoMadeUpText()
    {
        var courseId = Guid.NewGuid();
        var progress = new StudentCourseProgressRecord(Guid.NewGuid(), Guid.NewGuid(), courseId, 40m, null, 0);

        var handler = new Harness
        {
            Catalog = new FakeCatalogPriceContract([courseId], [], resolveTitles: false),
            Learning = new FakeLearningAnalyticsContract([progress]),
            Users = new FakeUserContactReader(resolveUsers: false),
        }.Build();

        var result = await handler.HandleAsync(Guid.NewGuid(), "30d", null, CancellationToken.None);

        var json = JsonSerializer.Serialize(result.Value);
        Assert.DoesNotContain("example.com", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Unknown Course", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("คอร์สเรียน", json);
        Assert.DoesNotContain("ผู้เรียน", json);
    }

    [Fact]
    public async Task HandleAsync_Student_UsesRealLastActiveCompletedEpisodesAndEpisodeTotal()
    {
        var courseId = Guid.NewGuid();
        var lastActive = new DateTime(2026, 9, 10, 8, 15, 0, DateTimeKind.Utc);
        // 50% progress on a 4-episode course would be "2" by estimate — the learner really finished 3.
        var progress = new StudentCourseProgressRecord(Guid.NewGuid(), Guid.NewGuid(), courseId, 50m, lastActive, 3);

        var episodes = Enumerable.Range(1, 4)
            .Select(i => new CourseEpisodeInfo(Guid.NewGuid(), courseId, $"Ep {i}", i))
            .ToList();

        var handler = new Harness
        {
            Catalog = new FakeCatalogPriceContract([courseId], episodes),
            Learning = new FakeLearningAnalyticsContract([progress]),
        }.Build();

        var result = await handler.HandleAsync(Guid.NewGuid(), "30d", null, CancellationToken.None);

        var student = Assert.Single(result.Value.Students);
        Assert.Equal(3, student.CompletedEpisodes);
        Assert.Equal(4, student.TotalEpisodes);
        Assert.Equal(lastActive, student.LastActiveAtUtc);
        Assert.StartsWith("Student ", student.Name);
        Assert.StartsWith("COURSE ", student.CourseTitle);
    }

    [Fact]
    public async Task HandleAsync_Student_CompletedEpisodesNeverExceedTheCoursesRealEpisodeCount()
    {
        var courseId = Guid.NewGuid();
        var progress = new StudentCourseProgressRecord(Guid.NewGuid(), Guid.NewGuid(), courseId, 100m, Now, 9);
        var episodes = new List<CourseEpisodeInfo> { new(Guid.NewGuid(), courseId, "Only", 1) };

        var handler = new Harness
        {
            Catalog = new FakeCatalogPriceContract([courseId], episodes),
            Learning = new FakeLearningAnalyticsContract([progress]),
        }.Build();

        var result = await handler.HandleAsync(Guid.NewGuid(), "30d", null, CancellationToken.None);

        var student = Assert.Single(result.Value.Students);
        Assert.Equal(1, student.CompletedEpisodes);
        Assert.Equal(1, student.TotalEpisodes);
    }

    [Fact]
    public async Task HandleAsync_CourseWithNoEpisodesOnRecord_ReportsZeroTotalNotAMadeUpOne()
    {
        var courseId = Guid.NewGuid();
        var progress = new StudentCourseProgressRecord(Guid.NewGuid(), Guid.NewGuid(), courseId, 0m, null, 0);

        var handler = new Harness
        {
            Catalog = new FakeCatalogPriceContract([courseId], []),
            Learning = new FakeLearningAnalyticsContract([progress]),
        }.Build();

        var result = await handler.HandleAsync(Guid.NewGuid(), "30d", null, CancellationToken.None);

        var student = Assert.Single(result.Value.Students);
        Assert.Equal(0, student.TotalEpisodes);
        Assert.Equal(0, student.CompletedEpisodes);
    }

    [Fact]
    public async Task HandleAsync_CourseOptions_ListResolvedTitles()
    {
        var courseId = Guid.NewGuid();
        var handler = new Harness { Catalog = new FakeCatalogPriceContract([courseId], []) }.Build();

        var result = await handler.HandleAsync(Guid.NewGuid(), "30d", null, CancellationToken.None);

        var option = Assert.Single(result.Value.Courses);
        Assert.Equal(courseId, option.Id);
        Assert.Equal($"COURSE {courseId}", option.Title);
    }

    // ---- KPIs --------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Kpis_NetRevenue_UsesTheProfileIdAndTheUtcMonthKeys()
    {
        var userId = Guid.NewGuid();
        var profileId = Guid.NewGuid();
        var revenue = new FakeInstructorRevenueReader(new()
        {
            [(profileId, "2026-10")] = 4500m,
            [(profileId, "2026-09")] = 3000m,
            [(userId, "2026-10")] = 999_999m, // the money of a (wrong) user-id key — must never be read
        });

        var handler = new Harness
        {
            CourseStats = new FakeInstructorCourseStatsReader(forAnyone: Profile(profileId, Stat())),
            Revenue = revenue,
        }.Build();

        var result = await handler.HandleAsync(userId, "30d", null, CancellationToken.None);

        var kpis = Assert.IsType<InstructorDashboardKpis>(result.Value.Kpis);
        Assert.Equal("2026-10", kpis.PeriodKey);
        Assert.Equal("2026-09", kpis.PreviousPeriodKey);
        Assert.Equal(4500m, kpis.NetRevenueThisMonth);
        Assert.Equal(3000m, kpis.NetRevenuePreviousMonth);
        Assert.Equal(50.0m, kpis.NetRevenueChangePercent);
        Assert.All(revenue.Calls, call => Assert.Equal(profileId, call.ProfileId));
        Assert.DoesNotContain(revenue.Calls, call => call.ProfileId == userId);
    }

    [Theory]
    [InlineData(2026, 12, 20, "2026-12", "2026-11")]
    [InlineData(2027, 1, 10, "2027-01", "2026-12")] // across the year boundary
    [InlineData(2026, 3, 31, "2026-03", "2026-02")] // the previous month is shorter than the current day-of-month
    public async Task Kpis_PeriodKeys_FollowTheClockAcrossMonthAndYearBoundaries(int year, int month, int day, string expectedCurrent, string expectedPrevious)
    {
        var handler = new Harness
        {
            CourseStats = new FakeInstructorCourseStatsReader(forAnyone: Profile(Guid.NewGuid(), Stat())),
            ClockNow = new DateTime(year, month, day, 23, 59, 0, DateTimeKind.Utc),
        }.Build();

        var result = await handler.HandleAsync(Guid.NewGuid(), "30d", null, CancellationToken.None);

        var kpis = Assert.IsType<InstructorDashboardKpis>(result.Value.Kpis);
        Assert.Equal(expectedCurrent, kpis.PeriodKey);
        Assert.Equal(expectedPrevious, kpis.PreviousPeriodKey);
    }

    [Theory]
    [InlineData(0, 500, null)] // nothing earned last month — nothing to compare with
    [InlineData(0, 0, null)]
    [InlineData(100, 150, 50.0)]
    [InlineData(100, 75, -25.0)]
    [InlineData(300, 100, -66.7)]
    [InlineData(100, 0, -100.0)]
    [InlineData(-100, 0, 100.0)] // a net-negative previous month (refund adjustments): going to zero is an improvement
    public async Task Kpis_NetRevenueChangePercent_IsRelativeToThePreviousMonth(double previous, double current, double? expected)
    {
        var profileId = Guid.NewGuid();
        var handler = new Harness
        {
            CourseStats = new FakeInstructorCourseStatsReader(forAnyone: Profile(profileId, Stat())),
            Revenue = new FakeInstructorRevenueReader(new()
            {
                [(profileId, "2026-10")] = (decimal)current,
                [(profileId, "2026-09")] = (decimal)previous,
            }),
        }.Build();

        var result = await handler.HandleAsync(Guid.NewGuid(), "30d", null, CancellationToken.None);

        var kpis = Assert.IsType<InstructorDashboardKpis>(result.Value.Kpis);
        Assert.Equal(expected is null ? null : (decimal)expected, kpis.NetRevenueChangePercent);
    }

    [Fact]
    public async Task Kpis_Rating_IsWeightedByRatingCountAcrossCourses()
    {
        // 5.0 x 10 and 4.0 x 30 => (50 + 120) / 40 = 4.25 — an unweighted mean would say 4.5.
        var handler = new Harness
        {
            CourseStats = new FakeInstructorCourseStatsReader(forAnyone: Profile(
                Guid.NewGuid(),
                Stat(ratingAverage: 5.0m, ratingCount: 10),
                Stat(ratingAverage: 4.0m, ratingCount: 30),
                Stat(ratingAverage: 0m, ratingCount: 0))), // an unrated course must not drag the average
        }.Build();

        var result = await handler.HandleAsync(Guid.NewGuid(), "30d", null, CancellationToken.None);

        var kpis = Assert.IsType<InstructorDashboardKpis>(result.Value.Kpis);
        Assert.Equal(4.25m, kpis.RatingAverage);
        Assert.Equal(40, kpis.RatingCount);
    }

    [Fact]
    public async Task Kpis_Rating_NothingRatedYet_IsNullNotZero()
    {
        var handler = new Harness
        {
            CourseStats = new FakeInstructorCourseStatsReader(forAnyone: Profile(Guid.NewGuid(), Stat(ratingAverage: 0m, ratingCount: 0))),
        }.Build();

        var result = await handler.HandleAsync(Guid.NewGuid(), "30d", null, CancellationToken.None);

        var kpis = Assert.IsType<InstructorDashboardKpis>(result.Value.Kpis);
        Assert.Null(kpis.RatingAverage);
        Assert.Equal(0, kpis.RatingCount);
    }

    [Fact]
    public async Task Kpis_CourseCounts_SplitPublishedFromTotal()
    {
        var handler = new Harness
        {
            CourseStats = new FakeInstructorCourseStatsReader(forAnyone: Profile(
                Guid.NewGuid(),
                Stat("Published"),
                Stat("Published"),
                Stat("Draft"),
                Stat("InReview"),
                Stat("Archived"))),
        }.Build();

        var result = await handler.HandleAsync(Guid.NewGuid(), "30d", null, CancellationToken.None);

        var kpis = Assert.IsType<InstructorDashboardKpis>(result.Value.Kpis);
        Assert.Equal(2, kpis.PublishedCourseCount);
        Assert.Equal(5, kpis.TotalCourseCount);
    }

    [Fact]
    public async Task Kpis_Students_AreTheContractsDistinctLearnerCountsOverAllTheInstructorsCourses_AndTheLast7Days()
    {
        var courseA = Guid.NewGuid();
        var courseB = Guid.NewGuid();
        var learning = new FakeLearningAnalyticsContract([], learnerCounts: new LearnerCounts(1280, 82));

        var handler = new Harness
        {
            Catalog = new FakeCatalogPriceContract([courseA, courseB], []),
            CourseStats = new FakeInstructorCourseStatsReader(forAnyone: Profile(Guid.NewGuid(), Stat(id: courseA), Stat(id: courseB))),
            Learning = learning,
        }.Build();

        // A filter to one course narrows the analytics numbers but NOT the account-wide headline numbers.
        var result = await handler.HandleAsync(Guid.NewGuid(), "7d", courseA, CancellationToken.None);

        var kpis = Assert.IsType<InstructorDashboardKpis>(result.Value.Kpis);
        Assert.Equal(1280, kpis.TotalStudents);
        Assert.Equal(82, kpis.NewStudentsLast7Days);
        Assert.Equal(new[] { courseA, courseB }.Order(), learning.RequestedLearnerCourseIds!.Order());
        Assert.Equal(Now.AddDays(-7), learning.RequestedLearnerSinceUtc);
    }

    [Fact]
    public async Task Kpis_UserWithoutAnInstructorProfile_AreZeroAndNullWithoutAskingForRevenue()
    {
        var revenue = new FakeInstructorRevenueReader(new() { [(Guid.Empty, "2026-10")] = 1m });
        var handler = new Harness { Revenue = revenue }.Build(); // default stats reader: no profile, no courses

        var result = await handler.HandleAsync(Guid.NewGuid(), "30d", null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var kpis = Assert.IsType<InstructorDashboardKpis>(result.Value.Kpis);
        Assert.Equal(0m, kpis.NetRevenueThisMonth);
        Assert.Equal(0m, kpis.NetRevenuePreviousMonth);
        Assert.Null(kpis.NetRevenueChangePercent);
        Assert.Equal(0, kpis.TotalStudents);
        Assert.Equal(0, kpis.NewStudentsLast7Days);
        Assert.Equal(0, kpis.PublishedCourseCount);
        Assert.Equal(0, kpis.TotalCourseCount);
        Assert.Null(kpis.RatingAverage);
        Assert.Equal(0, kpis.RatingCount);
        Assert.Empty(revenue.Calls);
        Assert.Empty(result.Value.CourseSummaries!);
        Assert.Null(result.Value.NextSession);
        Assert.Empty(result.Value.LiveAttendance!);
    }

    // ---- Course summaries ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task CourseSummaries_AreTheInstructorsOwnCoursesWithTheirRealStatus_AtMost50()
    {
        var courses = Enumerable.Range(1, 60)
            .Select(i => Stat(i % 2 == 0 ? "Draft" : "Published", title: $"Course {i}"))
            .ToArray();

        var handler = new Harness { CourseStats = new FakeInstructorCourseStatsReader(forAnyone: Profile(Guid.NewGuid(), courses)) }.Build();

        var result = await handler.HandleAsync(Guid.NewGuid(), "30d", null, CancellationToken.None);

        var summaries = result.Value.CourseSummaries!;
        Assert.Equal(50, summaries.Count);
        Assert.Equal("Course 1", summaries[0].Title); // the reader's order (newest first) is kept
        Assert.Equal("Published", summaries[0].Status);
        Assert.Equal("Draft", summaries[1].Status); // a Draft is shown as Draft — never as a hard-coded "Published"
        Assert.Equal(60, result.Value.Kpis!.TotalCourseCount);
    }

    [Fact]
    public async Task CourseSummaries_UnratedCourse_HasNullRatingNotZero()
    {
        var handler = new Harness
        {
            CourseStats = new FakeInstructorCourseStatsReader(forAnyone: Profile(
                Guid.NewGuid(),
                Stat(ratingAverage: 0m, ratingCount: 0, title: "Unrated"),
                Stat(ratingAverage: 4.8m, ratingCount: 12, title: "Rated"))),
        }.Build();

        var result = await handler.HandleAsync(Guid.NewGuid(), "30d", null, CancellationToken.None);

        var summaries = result.Value.CourseSummaries!;
        Assert.Null(summaries.Single(s => s.Title == "Unrated").RatingAverage);
        Assert.Equal(4.8m, summaries.Single(s => s.Title == "Rated").RatingAverage);
    }

    // ---- Next session ----------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task NextSession_ASessionBeingTaughtNow_IsReportedAsLive_WithTheRealInviteAndRoomFacts()
    {
        var userId = Guid.NewGuid();
        var running = Session(userId, Now.AddMinutes(-20)); // started 20 minutes ago, ends in 100
        var later = Session(userId, Now.AddDays(2));
        var attendance = new FakeLiveAttendanceReader(new() { [running.SessionId] = new LiveSessionStats(running.SessionId, 18, 11, true) });

        var handler = new Harness
        {
            Catalog = OwnsACourse(),
            CourseStats = new FakeInstructorCourseStatsReader(forAnyone: Profile(Guid.NewGuid(), Stat())),
            Schedule = new FakeLiveScheduleReader([later, running]),
            Attendance = attendance,
        }.Build();

        var result = await handler.HandleAsync(userId, "30d", null, CancellationToken.None);

        var next = Assert.IsType<InstructorNextSession>(result.Value.NextSession);
        Assert.Equal(running.SessionId, next.SessionId);
        Assert.Equal(LiveSessionDisplayState.Live, next.DisplayState);
        Assert.Equal(18, next.ExpectedLearners);
        Assert.True(next.MeetingUsable);
        Assert.Equal(running.StartsAtUtc, next.StartsAtUtc);
        Assert.Equal(running.EndsAtUtc, next.EndsAtUtc);
        Assert.Equal("คอร์สสด", next.CourseTitle);
    }

    [Fact]
    public async Task NextSession_AFutureSession_IsUpcoming_AndAMissingRoomIsReportedNotUsable()
    {
        var userId = Guid.NewGuid();
        var future = Session(userId, Now.AddDays(3));

        var handler = new Harness
        {
            Catalog = OwnsACourse(),
            CourseStats = new FakeInstructorCourseStatsReader(forAnyone: Profile(Guid.NewGuid(), Stat())),
            Schedule = new FakeLiveScheduleReader([future]),
        }.Build();

        var result = await handler.HandleAsync(userId, "30d", null, CancellationToken.None);

        var next = Assert.IsType<InstructorNextSession>(result.Value.NextSession);
        Assert.Equal(LiveSessionDisplayState.Upcoming, next.DisplayState);
        Assert.False(next.MeetingUsable);
        Assert.Equal(0, next.ExpectedLearners);
    }

    [Fact]
    public async Task NextSession_NoSessionLeft_IsNull_AndCancelledOrFinishedOnesNeverCount()
    {
        var userId = Guid.NewGuid();
        var handler = new Harness
        {
            Catalog = OwnsACourse(),
            CourseStats = new FakeInstructorCourseStatsReader(forAnyone: Profile(Guid.NewGuid(), Stat())),
            Schedule = new FakeLiveScheduleReader(
            [
                Session(userId, Now.AddDays(2), status: LiveSessionStatus.Cancelled), // cancelled
                Session(userId, Now.AddDays(-2)), // already finished
            ]),
        }.Build();

        var result = await handler.HandleAsync(userId, "30d", null, CancellationToken.None);

        Assert.Null(result.Value.NextSession);
    }

    [Fact]
    public async Task LiveSection_AsksForTheNextSessionWithOverlapSemanticsAndExcludesCancelled()
    {
        var userId = Guid.NewGuid();
        var schedule = new FakeLiveScheduleReader([]);
        var handler = new Harness
        {
            Catalog = OwnsACourse(),
            CourseStats = new FakeInstructorCourseStatsReader(forAnyone: Profile(Guid.NewGuid(), Stat())),
            Schedule = schedule,
        }.Build();

        await handler.HandleAsync(userId, "30d", null, CancellationToken.None);

        Assert.Equal(2, schedule.InstructorCalls.Count);
        var next = schedule.InstructorCalls[0];
        Assert.Equal((userId, (DateTime?)Now, (DateTime?)null, false, false, 0, 1), next);
        var past = schedule.InstructorCalls[1];
        Assert.Equal(userId, past.UserId);
        Assert.Null(past.From);
        Assert.Equal(Now, past.To);
        Assert.False(past.IncludeCancelled);
        Assert.True(past.NewestFirst);
    }

    // ---- Live attendance ---------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task LiveAttendance_IsTheLatestFiveFinishedSessions_NewestFirst_WithRealRates()
    {
        var userId = Guid.NewGuid();
        var finished = Enumerable.Range(1, 7)
            .Select(i => Session(userId, Now.AddDays(-i), TimeSpan.FromHours(1), title: $"Session {i}")) // Session 1 = yesterday ... Session 7 = a week ago
            .ToList();

        var stats = finished.ToDictionary(s => s.SessionId, s => new LiveSessionStats(s.SessionId, 3, 2, true));
        var attendance = new FakeLiveAttendanceReader(stats);

        var handler = new Harness
        {
            Catalog = OwnsACourse(),
            CourseStats = new FakeInstructorCourseStatsReader(forAnyone: Profile(Guid.NewGuid(), Stat())),
            Schedule = new FakeLiveScheduleReader(finished),
            Attendance = attendance,
        }.Build();

        var result = await handler.HandleAsync(userId, "30d", null, CancellationToken.None);

        var items = result.Value.LiveAttendance!;
        Assert.Equal(5, items.Count);
        Assert.Equal(["Session 1", "Session 2", "Session 3", "Session 4", "Session 5"], items.Select(i => i.Title));
        Assert.All(items, item =>
        {
            Assert.Equal(3, item.ExpectedLearners);
            Assert.Equal(2, item.JoinedLearners);
            Assert.Equal(66.7m, item.AttendanceRatePercent); // 2 * 100 / 3
        });
    }

    [Fact]
    public async Task LiveAttendance_NobodyInvited_HasNullRateNotZeroOrADivisionError()
    {
        var userId = Guid.NewGuid();
        var session = Session(userId, Now.AddDays(-1), TimeSpan.FromHours(1));
        var handler = new Harness
        {
            Catalog = OwnsACourse(),
            CourseStats = new FakeInstructorCourseStatsReader(forAnyone: Profile(Guid.NewGuid(), Stat())),
            Schedule = new FakeLiveScheduleReader([session]),
            Attendance = new FakeLiveAttendanceReader(new() { [session.SessionId] = new LiveSessionStats(session.SessionId, 0, 4, true) }),
        }.Build();

        var result = await handler.HandleAsync(userId, "30d", null, CancellationToken.None);

        var item = Assert.Single(result.Value.LiveAttendance!);
        Assert.Equal(0, item.ExpectedLearners);
        Assert.Equal(4, item.JoinedLearners);
        Assert.Null(item.AttendanceRatePercent);
    }

    [Fact]
    public async Task LiveAttendance_LeavesOutCancelledStillRunningAndNotStartedSessions()
    {
        var userId = Guid.NewGuid();
        var finished = Session(userId, Now.AddDays(-1), TimeSpan.FromHours(1), title: "Finished");
        var handler = new Harness
        {
            Catalog = OwnsACourse(),
            CourseStats = new FakeInstructorCourseStatsReader(forAnyone: Profile(Guid.NewGuid(), Stat())),
            Schedule = new FakeLiveScheduleReader(
            [
                finished,
                Session(userId, Now.AddDays(-1), TimeSpan.FromHours(1), status: LiveSessionStatus.Cancelled, title: "Cancelled"),
                Session(userId, Now.AddMinutes(-30), TimeSpan.FromHours(2), title: "Running"),
                Session(userId, Now.AddDays(1), title: "Future"),
            ]),
        }.Build();

        var result = await handler.HandleAsync(userId, "30d", null, CancellationToken.None);

        var item = Assert.Single(result.Value.LiveAttendance!);
        Assert.Equal("Finished", item.Title);
        Assert.Equal("Running", result.Value.NextSession!.Title); // the running one is the "next" (in progress) session instead
    }

    [Fact]
    public async Task LiveSection_ReadsEverySessionsStatsInOneBatchedCall()
    {
        var userId = Guid.NewGuid();
        var sessions = new[]
        {
            Session(userId, Now.AddDays(1)),
            Session(userId, Now.AddDays(-1), TimeSpan.FromHours(1)),
            Session(userId, Now.AddDays(-2), TimeSpan.FromHours(1)),
        };
        var attendance = new FakeLiveAttendanceReader();

        var handler = new Harness
        {
            Catalog = OwnsACourse(),
            CourseStats = new FakeInstructorCourseStatsReader(forAnyone: Profile(Guid.NewGuid(), Stat())),
            Schedule = new FakeLiveScheduleReader(sessions),
            Attendance = attendance,
        }.Build();

        await handler.HandleAsync(userId, "30d", null, CancellationToken.None);

        var call = Assert.Single(attendance.StatsCalls);
        Assert.Equal(sessions.Select(s => s.SessionId).Order(), call.Order());
    }

    [Fact]
    public async Task LiveSection_NoSessionsAtAll_DoesNotAskTheAttendanceReader()
    {
        var attendance = new FakeLiveAttendanceReader();
        var handler = new Harness
        {
            Catalog = OwnsACourse(),
            CourseStats = new FakeInstructorCourseStatsReader(forAnyone: Profile(Guid.NewGuid(), Stat())),
            Attendance = attendance,
        }.Build();

        var result = await handler.HandleAsync(Guid.NewGuid(), "30d", null, CancellationToken.None);

        Assert.Empty(attendance.StatsCalls);
        Assert.Null(result.Value.NextSession);
        Assert.Empty(result.Value.LiveAttendance!);
    }

    // ---- IDOR: an instructor sees only their own data ------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Idor_InstructorB_NeverSeesInstructorAsCoursesSessionsOrRevenue()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var profileA = Guid.NewGuid();
        var profileB = Guid.NewGuid();
        var courseA = Guid.NewGuid();
        var courseB = Guid.NewGuid();

        var sessionA = Session(userA, Now.AddDays(1), title: "A upcoming");
        var sessionA2 = Session(userA, Now.AddDays(-1), TimeSpan.FromHours(1), title: "A finished");
        var sessionB = Session(userB, Now.AddDays(2), title: "B upcoming");

        var revenue = new FakeInstructorRevenueReader(new()
        {
            [(profileA, "2026-10")] = 90_000m,
            [(profileB, "2026-10")] = 1_000m,
        });

        var learning = new FakeLearningAnalyticsContract([
            new StudentCourseProgressRecord(Guid.NewGuid(), Guid.NewGuid(), courseA, 10m, null, 0),
            new StudentCourseProgressRecord(Guid.NewGuid(), Guid.NewGuid(), courseB, 20m, null, 0),
        ]);

        var handler = new Harness
        {
            Catalog = new FakeCatalogPriceContract([], [], ownedByUser: new() { [userA] = [courseA], [userB] = [courseB] }),
            CourseStats = new FakeInstructorCourseStatsReader(byUser: new()
            {
                [userA] = Profile(profileA, Stat(title: "A course", id: courseA)),
                [userB] = Profile(profileB, Stat(title: "B course", id: courseB)),
            }),
            Revenue = revenue,
            Learning = learning,
            Schedule = new FakeLiveScheduleReader([sessionA, sessionA2, sessionB]),
        }.Build();

        var resultB = await handler.HandleAsync(userB, "30d", null, CancellationToken.None);

        var response = resultB.Value;
        Assert.Equal(1_000m, response.Kpis!.NetRevenueThisMonth);
        Assert.Equal("B course", Assert.Single(response.CourseSummaries!).Title);
        Assert.Equal(courseB, Assert.Single(response.Courses).Id);
        Assert.Equal($"COURSE {courseB}", Assert.Single(response.Students).CourseTitle);
        Assert.Equal("B upcoming", response.NextSession!.Title);
        Assert.Empty(response.LiveAttendance!);
        Assert.DoesNotContain(revenue.Calls, call => call.ProfileId == profileA);
        Assert.DoesNotContain(learning.RequestedLearnerCourseIds!, id => id == courseA);
    }

    [Fact]
    public async Task Idor_ACourseIdFilterThatIsNotTheCallersOwn_IsIgnoredNotHonoured()
    {
        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var courseA = Guid.NewGuid();
        var courseB = Guid.NewGuid();
        var learning = new FakeLearningAnalyticsContract([]);

        var handler = new Harness
        {
            Catalog = new FakeCatalogPriceContract([], [], ownedByUser: new() { [userA] = [courseA], [userB] = [courseB] }),
            Learning = learning,
        }.Build();

        // B asks for A's course: the filter is dropped and only B's own courses are analysed.
        await handler.HandleAsync(userB, "30d", courseA, CancellationToken.None);

        Assert.Equal(new[] { courseB }, learning.RequestedWatchedCourseIds!);
    }

    [Fact]
    public async Task Idor_AUserWithoutAProfile_GetsNoOtherInstructorsLiveSessions()
    {
        var instructor = Guid.NewGuid();
        var stranger = Guid.NewGuid();
        var handler = new Harness
        {
            Schedule = new FakeLiveScheduleReader([Session(instructor, Now.AddDays(1))]),
        }.Build();

        var result = await handler.HandleAsync(stranger, "30d", null, CancellationToken.None);

        Assert.Null(result.Value.NextSession);
        Assert.Empty(result.Value.LiveAttendance!);
    }
}
