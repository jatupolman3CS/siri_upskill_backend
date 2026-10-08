using System.Data.Common;
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Features.GetCourseLiveSchedule;
using Siri.Modules.Commerce.Infrastructure;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Learning.Infrastructure;
using Siri.Modules.Learning.Infrastructure.Contracts;
using Siri.Modules.Live.Domain;
using Siri.Modules.Live.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.UnitTests.Live;

/// <summary>
/// Proves that every EF query added for P11-05 can actually be TRANSLATED to SQL — without a database (the same technique as <see cref="LiveQueryTranslationTests"/>).
/// EF translates a query before it opens a connection, so against an unreachable server a translatable query fails with the connection-opening interceptor firing,
/// while an untranslatable one fails with an <see cref="InvalidOperationException"/> that says so. What the SQL actually does (grouping, distinct counts, paging) is covered by
/// the Testcontainers integration tests; this guards the part a machine without Docker can still check.
/// </summary>
public class LiveJoinQueryTranslationTests
{
    private static readonly Guid SomeId = Guid.NewGuid();
    private static readonly Guid[] SomeIds = [SomeId, Guid.NewGuid()];

    private static AppDbContext Context() =>
        new(new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=127.0.0.1;Port=1;Database=unit-test;Username=none;Password=none;Pooling=false")
            .AddInterceptors(new StopAtConnectionOpen())
            .Options);

    private sealed class ReachedDatabaseException : Exception;

    private sealed class StopAtConnectionOpen : DbConnectionInterceptor
    {
        public override InterceptionResult ConnectionOpening(DbConnection connection, ConnectionEventData eventData, InterceptionResult result) =>
            throw new ReachedDatabaseException();

        public override ValueTask<InterceptionResult> ConnectionOpeningAsync(
            DbConnection connection, ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default) =>
            throw new ReachedDatabaseException();
    }

    private static async Task AssertTranslatesAsync(Func<AppDbContext, Task> query)
    {
        using var context = Context();

        var exception = await Record.ExceptionAsync(() => query(context));

        Assert.NotNull(exception);
        Assert.True(Chain(exception).Any(e => e is ReachedDatabaseException), exception.ToString());
    }

    private static IEnumerable<Exception> Chain(Exception exception)
    {
        for (var current = exception; current is not null; current = current.InnerException)
        {
            yield return current;
        }
    }

    // ---- Live: join log, invites, attendance -------------------------------------------------------------------

    [Fact]
    public Task JoinLog_GetJoinedLearnerIds_Translates() =>
        AssertTranslatesAsync(c => new SessionJoinLogRepository(c).GetJoinedLearnerIdsAsync(SomeId, CancellationToken.None));

    [Fact]
    public Task JoinLog_GetLearnerJoinSummaries_Translates() =>
        AssertTranslatesAsync(c => new SessionJoinLogRepository(c).GetLearnerJoinSummariesAsync(SomeId, SomeIds, CancellationToken.None));

    [Fact]
    public async Task JoinLog_GetLearnerJoinSummaries_WithNoUsers_AsksNothing()
    {
        using var context = Context();

        // Would fail with a connection error if it queried: an empty id list short-circuits.
        var result = await new SessionJoinLogRepository(context).GetLearnerJoinSummariesAsync(SomeId, [], CancellationToken.None);

        Assert.Empty(result);
    }

    [Fact]
    public Task Invites_GetStatusesForUser_Translates() =>
        AssertTranslatesAsync(c => new SessionInviteReader(c).GetStatusesForUserAsync(SomeId, SomeIds, CancellationToken.None));

    [Fact]
    public Task Invites_GetLearnerStatusesForSession_Translates() =>
        AssertTranslatesAsync(c => new SessionInviteReader(c).GetLearnerStatusesForSessionAsync(SomeId, CancellationToken.None));

    [Fact]
    public Task Attendance_GetCourseIdsAttended_Translates() =>
        AssertTranslatesAsync(c => new LiveAttendanceReader(c).GetCourseIdsAttendedAsync(SomeId, SomeIds, CancellationToken.None));

    [Fact]
    public Task Attendance_CountExpectedLearners_Translates() =>
        AssertTranslatesAsync(c => new LiveAttendanceReader(c).CountExpectedLearnersAsync(SomeIds, CancellationToken.None));

    [Fact]
    public Task Attendance_CountJoinedLearners_DistinctPerSession_Translates() =>
        AssertTranslatesAsync(c => new LiveAttendanceReader(c).CountJoinedLearnersAsync(SomeIds, CancellationToken.None));

    [Fact]
    public Task Attendance_FindUsableMeetings_Translates() =>
        AssertTranslatesAsync(c => new LiveAttendanceReader(c).FindUsableMeetingsAsync(SomeIds, CancellationToken.None));

    [Fact]
    public async Task Attendance_NoIds_AreAnsweredWithoutTouchingTheDatabase()
    {
        using var context = Context();
        var reader = new LiveAttendanceReader(context);

        Assert.Empty(await reader.GetSessionStatsAsync([], CancellationToken.None));
        Assert.Empty(await reader.GetCourseIdsAttendedAsync(SomeId, [], CancellationToken.None));
        Assert.Empty(await reader.GetCourseIdsAttendedAsync(Guid.Empty, SomeIds, CancellationToken.None));
    }

    // ---- The IsUsable expression stays in step with the property ---------------------------------------------------

    /// <summary>Meetings in every state the state machine can reach, with and without a stored link.</summary>
    private static List<SESSION_MEETING> ReachableMeetings()
    {
        var clock = new FakeClock(LiveTestData.Now);
        var meetings = new List<SESSION_MEETING>();

        SESSION_MEETING Google()
        {
            var m = SESSION_MEETING.Stage(Guid.NewGuid());
            m.RecordGoogleSynced(MeetingProvider.GoogleMeet, "event-1", "cipher", Guid.NewGuid(), clock);
            return m;
        }

        SESSION_MEETING Failing(SESSION_MEETING m)
        {
            for (var i = 0; i < SESSION_MEETING.MaxAttempts; i++)
            {
                m.RecordAttemptFailed("google_transient", clock);
            }

            return m;
        }

        meetings.Add(SESSION_MEETING.Stage(Guid.NewGuid())); // Pending, no link

        var pendingWithLink = Google();
        pendingWithLink.MarkSessionChanged(); // Google event exists -> Pending (patch), link kept
        meetings.Add(pendingWithLink);

        var awaiting = SESSION_MEETING.Stage(Guid.NewGuid());
        awaiting.ResolveAsAwaitingLink();
        meetings.Add(awaiting);

        meetings.Add(Google()); // Synced with a link

        var manual = SESSION_MEETING.Stage(Guid.NewGuid());
        manual.SetManualLink("cipher"); // Synced with a link (manual)
        meetings.Add(manual);

        var reconnect = SESSION_MEETING.Stage(Guid.NewGuid());
        reconnect.RecordNeedsReconnect("invalid_grant"); // NeedsReconnect, no link
        meetings.Add(reconnect);

        var reconnectWithLink = Google();
        reconnectWithLink.RecordNeedsReconnect("invalid_grant"); // NeedsReconnect, link kept
        meetings.Add(reconnectWithLink);

        meetings.Add(Failing(SESSION_MEETING.Stage(Guid.NewGuid()))); // Failed, no link

        var failedWithLink = Google();
        failedWithLink.MarkSessionChanged();
        meetings.Add(Failing(failedWithLink)); // Failed, link kept

        var pendingDelete = Google();
        pendingDelete.MarkSessionCancelled(); // PendingDelete, link still stored
        meetings.Add(pendingDelete);

        var deletedNoLink = SESSION_MEETING.Stage(Guid.NewGuid());
        deletedNoLink.MarkDeleted();
        meetings.Add(deletedNoLink);

        var deletedWithLink = SESSION_MEETING.Stage(Guid.NewGuid());
        deletedWithLink.SetManualLink("cipher");
        deletedWithLink.MarkSessionCancelled(); // no Google event -> Deleted, ciphertext still stored
        meetings.Add(deletedWithLink);

        return meetings;
    }

    [Fact]
    public void MeetingUsability_Expression_AgreesWithTheEntitysIsUsable_ForEveryReachableState()
    {
        var meetings = ReachableMeetings();
        var expression = MeetingUsability.IsUsable.Compile();

        // The fixture itself must cover every sync status, with and without a link where reachable — otherwise it proves less than it claims.
        Assert.Equal(Enum.GetValues<MeetingSyncStatus>().Order(), meetings.Select(m => m.SYNC_STATUS).Distinct().Order());
        Assert.Contains(meetings, m => m.IsUsable);
        Assert.Contains(meetings, m => !m.IsUsable);
        Assert.Contains(meetings, m => m.SYNC_STATUS == MeetingSyncStatus.Deleted && m.MEET_URL_ENCRYPTED is not null);

        foreach (var meeting in meetings)
        {
            Assert.Equal(meeting.IsUsable, expression(meeting));
        }
    }

    // ---- Catalog: live-schedule ---------------------------------------------------------------------------------------

    [Fact]
    public Task CatalogLiveSchedule_FindCourse_Translates() =>
        AssertTranslatesAsync(c => new GetCourseLiveScheduleHandler(c).FindCourseAsync(SomeId, CancellationToken.None));

    [Fact]
    public Task CatalogLiveSchedule_FindInstructorProfileId_Translates() =>
        AssertTranslatesAsync(c => new GetCourseLiveScheduleHandler(c).FindInstructorProfileIdAsync(SomeId, CancellationToken.None));

    [Fact]
    public Task CatalogLiveSchedule_ListSessions_Translates() =>
        AssertTranslatesAsync(c => new GetCourseLiveScheduleHandler(c).ListSessionsAsync(SomeId, CancellationToken.None));

    // ---- Learning: active enrolled course ids -------------------------------------------------------------------------

    [Fact]
    public Task Learning_GetActiveEnrolledCourseIds_Translates() =>
        AssertTranslatesAsync(c => new LearningAccessContract(new EnrollmentRepository(c), new NoCatalog(), new FakeClock(LiveTestData.Now), new Siri.UnitTests.Learning.RecordingCourseEnrollmentCountUpdater())
            .GetActiveEnrolledCourseIdsAsync(SomeId, CancellationToken.None));

    // ---- Learning: learner counts and watched seconds (P11-10 section 2.3 contract methods) ----------------------------------

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public Task Learning_CountDistinctLearners_Translates(bool onlySince) =>
        AssertTranslatesAsync(c => new LearningAnalyticsContract(c, new NoCatalog())
            .CountDistinctLearnersAsync(SomeIds, onlySince ? new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc) : null, CancellationToken.None));

    [Fact]
    public Task Learning_GetWatchedSeconds_Translates() =>
        AssertTranslatesAsync(c => new LearningAnalyticsContract(c, new NoCatalog())
            .GetWatchedSecondsAsync(SomeIds, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc), CancellationToken.None));

    [Fact]
    public async Task Learning_LearnerCountsAndWatchedSeconds_WithNoCourses_AreZero_WithoutTouchingTheDatabase()
    {
        using var context = Context();
        var contract = new LearningAnalyticsContract(context, new NoCatalog());
        var since = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);

        Assert.Equal(new LearnerCounts(0, 0), await contract.GetLearnerCountsAsync([], since, CancellationToken.None));
        Assert.Equal(0L, await contract.GetWatchedSecondsAsync([], since, CancellationToken.None));
    }

    [Fact]
    public async Task Learning_TheInterfaceDefaults_ReportZeros_SoOtherImplementersKeepCompiling()
    {
        ILearningAnalyticsContract defaulted = new DefaultAnalytics();

        Assert.Equal(new LearnerCounts(0, 0), await defaulted.GetLearnerCountsAsync([SomeId], DateTime.UtcNow, CancellationToken.None));
        Assert.Equal(0L, await defaulted.GetWatchedSecondsAsync([SomeId], DateTime.UtcNow, CancellationToken.None));
    }

    /// <summary>Implements only the members that have no default — the two new ones must still exist.</summary>
    private sealed class DefaultAnalytics : ILearningAnalyticsContract
    {
        public Task<IReadOnlyList<EpisodeDropOffItem>> GetEpisodeDropOffRollupAsync(DateOnly date, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<DailyCourseActivityItem>> GetDailyCourseActivityAsync(DateOnly date, CancellationToken cancellationToken) =>
            throw new NotSupportedException();

        public Task<int> PurgeOldWatchEventsAsync(DateTime olderThanUtc, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<EpisodeWatchTimeBucket>> GetEpisodeWatchTimeBucketsAsync(IEnumerable<Guid> courseIds, DateTime activeSinceUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }

    // ---- Commerce: succeeded payment per order -----------------------------------------------------------------------------

    [Fact]
    public Task Commerce_GetSucceededPaymentIdsByOrderIds_Translates() =>
        AssertTranslatesAsync(c => new PaymentRepository(c).GetSucceededPaymentIdsByOrderIdsAsync(SomeIds, CancellationToken.None));

    [Fact]
    public async Task Commerce_GetSucceededPaymentIdsByOrderIds_NoIds_AsksNothing()
    {
        using var context = Context();

        Assert.Empty(await new PaymentRepository(context).GetSucceededPaymentIdsByOrderIdsAsync([], CancellationToken.None));
    }

    /// <summary>Not used by the query under test — every member fails loudly if a test accidentally relies on it.</summary>
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
}
