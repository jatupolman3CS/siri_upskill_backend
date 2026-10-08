using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Learning.Domain;
using Siri.Persistence;

namespace Siri.Modules.Learning.Infrastructure.Contracts;

public sealed class LearningAnalyticsContract(AppDbContext dbContext, ICatalogPriceContract catalogPriceContract) : ILearningAnalyticsContract
{
    public async Task<IReadOnlyList<EpisodeDropOffItem>> GetEpisodeDropOffRollupAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var startUtc = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endUtc = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var events = await (
            from w in dbContext.WatchEvents().AsNoTracking()
            join e in dbContext.Enrollments().AsNoTracking() on w.ENROLLMENT_ID equals e.ENROLLMENT_ID
            where w.OCCURRED_AT_UTC >= startUtc && w.OCCURRED_AT_UTC <= endUtc
            select new { w.ENROLLMENT_ID, w.EPISODE_ID, e.COURSE_ID, w.EVENT_TYPE, w.POSITION_SECONDS })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (events.Count == 0)
        {
            return [];
        }

        // Real episode durations (Catalog) turn "furthest position reached" into a real watch percentage.
        var courseIds = events.Select(e => e.COURSE_ID).Distinct().ToList();
        var episodes = await catalogPriceContract.GetEpisodesForCoursesAsync(courseIds, cancellationToken).ConfigureAwait(false);
        var durationByEpisode = episodes
            .Where(e => e.DurationSeconds is > 0)
            .ToDictionary(e => e.EpisodeId, e => e.DurationSeconds!.Value);

        var results = events
            .GroupBy(w => w.EPISODE_ID)
            .Select(g =>
            {
                var startCount = g.Count(w => w.EVENT_TYPE == WatchEventType.Play);
                var completeCount = g.Count(w => w.EVENT_TYPE == WatchEventType.Ended);

                // Never a made-up value: measured per viewer from real positions + real duration (see
                // WatchPercentCalculator). 0 only when no viewer of the episode is measurable.
                int? duration = durationByEpisode.TryGetValue(g.Key, out var seconds) ? seconds : null;
                var avgWatchPercent = WatchPercentCalculator.AveragePercent(
                    g.Select(w => new WatchSample(w.ENROLLMENT_ID, w.EVENT_TYPE, w.POSITION_SECONDS)),
                    duration);

                return new EpisodeDropOffItem(g.Key, startCount, completeCount, avgWatchPercent);
            })
            .ToList();

        return results;
    }

    public async Task<IReadOnlyList<EpisodeWatchTimeBucket>> GetEpisodeWatchTimeBucketsAsync(
        IEnumerable<Guid> courseIds,
        DateTime activeSinceUtc,
        CancellationToken cancellationToken)
    {
        var idList = courseIds.Distinct().ToList();
        if (idList.Count == 0)
        {
            return [];
        }

        return await (
            from p in dbContext.EpisodeProgresses().AsNoTracking()
            join e in dbContext.Enrollments().AsNoTracking() on p.ENROLLMENT_ID equals e.ENROLLMENT_ID
            where idList.Contains(e.COURSE_ID) && p.UPDATED_AT_UTC >= activeSinceUtc && p.WATCHED_SECONDS > 0
            group p by new { p.EPISODE_ID, p.WATCHED_SECONDS } into g
            select new EpisodeWatchTimeBucket(g.Key.EPISODE_ID, g.Key.WATCHED_SECONDS, g.Count()))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LearnerCounts> GetLearnerCountsAsync(
        IReadOnlyCollection<Guid> courseIds,
        DateTime sinceUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(courseIds);

        var ids = courseIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new LearnerCounts(0, 0);
        }

        var total = await CountDistinctLearnersAsync(ids, enrolledSinceUtc: null, cancellationToken).ConfigureAwait(false);
        var since = await CountDistinctLearnersAsync(ids, sinceUtc, cancellationToken).ConfigureAwait(false);

        return new LearnerCounts(total, since);
    }

    /// <summary>Different learners with an Active or Expired enrollment in the courses (a Revoked one is not a learner), optionally only those who enrolled at or after
    /// <paramref name="enrolledSinceUtc"/>. DISTINCT per user, so someone in several of the courses counts once. Internal so a unit test can prove both shapes translate to SQL.</summary>
    internal async Task<int> CountDistinctLearnersAsync(Guid[] courseIds, DateTime? enrolledSinceUtc, CancellationToken cancellationToken)
    {
        var query = dbContext.Enrollments()
            .AsNoTracking()
            .Where(e => courseIds.Contains(e.COURSE_ID) && (e.STATUS == EnrollmentStatus.Active || e.STATUS == EnrollmentStatus.Expired));

        if (enrolledSinceUtc is { } since)
        {
            query = query.Where(e => e.ENROLLED_AT_UTC >= since);
        }

        return await query.Select(e => e.USER_ID).Distinct().CountAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<long> GetWatchedSecondsAsync(
        IReadOnlyCollection<Guid> courseIds,
        DateTime sinceUtc,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(courseIds);

        var ids = courseIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return 0;
        }

        // Summed as long: a busy course can exceed int.MaxValue seconds in total.
        var seconds = await (
            from p in dbContext.EpisodeProgresses().AsNoTracking()
            join e in dbContext.Enrollments().AsNoTracking() on p.ENROLLMENT_ID equals e.ENROLLMENT_ID
            where ids.Contains(e.COURSE_ID) && p.UPDATED_AT_UTC >= sinceUtc
            select (long?)p.WATCHED_SECONDS)
            .SumAsync(cancellationToken)
            .ConfigureAwait(false);

        return seconds ?? 0L;
    }

    public async Task<IReadOnlyList<DailyCourseActivityItem>> GetDailyCourseActivityAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var startUtc = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endUtc = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var enrollments = await dbContext.Enrollments()
            .AsNoTracking()
            .Where(e => e.ENROLLED_AT_UTC >= startUtc && e.ENROLLED_AT_UTC <= endUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        var watchEvents = await (
            from w in dbContext.WatchEvents().AsNoTracking()
            join e in dbContext.Enrollments().AsNoTracking() on w.ENROLLMENT_ID equals e.ENROLLMENT_ID
            where w.OCCURRED_AT_UTC >= startUtc && w.OCCURRED_AT_UTC <= endUtc
            select new { e.COURSE_ID, w.WATCH_EVENT_ID }
        ).ToListAsync(cancellationToken).ConfigureAwait(false);

        var courseIds = enrollments.Select(e => e.COURSE_ID)
            .Union(watchEvents.Select(w => w.COURSE_ID))
            .Distinct()
            .ToList();

        if (courseIds.Count == 0)
        {
            return [];
        }

        var list = new List<DailyCourseActivityItem>();

        foreach (var courseId in courseIds)
        {
            var views = watchEvents.Count(w => w.COURSE_ID == courseId);
            var newEnrollments = enrollments.Count(e => e.COURSE_ID == courseId);
            var completedCount = enrollments.Count(e => e.COURSE_ID == courseId && e.COMPLETED_AT_UTC != null);
            var completionRate = newEnrollments > 0
                ? Math.Round((decimal)completedCount * 100m / newEnrollments, 2)
                : 0m;

            list.Add(new DailyCourseActivityItem(courseId, views, newEnrollments, completionRate));
        }

        return list;
    }

    /// <summary>Page size for <see cref="PurgeOldWatchEventsAsync"/>'s batched delete loop — see that
    /// method's own doc comment for why this is batched at all.</summary>
    private const int BatchSize = 10_000;

    /// <summary>
    /// Query-performance audit (2026-09): this used to <c>.ToListAsync()</c> every matching row as a
    /// tracked entity, then <c>RemoveRange()</c> + one <c>SaveChangesAsync()</c> — with 90 days of
    /// retention on this database's largest table (one row per video-heartbeat event, see
    /// <see cref="WATCH_EVENT"/>'s own doc comment), that risked materializing a huge row set into
    /// process memory and holding one giant delete transaction/lock.
    /// <para>
    /// Fixed by batching a set-based <c>ExecuteDeleteAsync()</c> (the established atomic-write pattern
    /// this codebase already uses for bulk operations — see <c>PromoCodeRepository</c>'s
    /// <c>ExecuteUpdateAsync</c> usage, and <c>Siri.Modules.Identity.Infrastructure
    /// .DataRetentionCleanupJob</c>'s own unbatched <c>ExecuteDeleteAsync</c> for a smaller, much
    /// lower-write-traffic table): each loop iteration selects one page of PKs older than the cutoff
    /// (bounded memory — just <c>bigint</c> ids, never full rows) and deletes only that page. Batching
    /// (rather than one unbounded <c>ExecuteDeleteAsync</c>, as <c>DataRetentionCleanupJob</c> does) is
    /// deliberate here specifically because, unlike that job's tables, WATCH_EVENTS receives continuous
    /// concurrent INSERT traffic from every learner actively watching a video right now — one huge DELETE
    /// could accumulate enough row/page locks to trigger SQL Server's lock escalation to a table-level
    /// lock (~5,000 locks on one statement) and block those live inserts. Each batch is its own short
    /// implicit transaction (<c>ExecuteDeleteAsync</c> does not span calls — see Microsoft Learn's
    /// "ExecuteUpdate and ExecuteDelete" docs, "Transactions" section), so at most <see cref="BatchSize"/>
    /// rows are ever locked at once, and live inserts can interleave between batches.
    /// </para>
    /// </summary>
    public async Task<int> PurgeOldWatchEventsAsync(DateTime olderThanUtc, CancellationToken cancellationToken)
    {
        var totalDeleted = 0;

        while (true)
        {
            var batchIds = await dbContext.WatchEvents()
                .Where(w => w.OCCURRED_AT_UTC < olderThanUtc)
                .OrderBy(w => w.WATCH_EVENT_ID)
                .Select(w => w.WATCH_EVENT_ID)
                .Take(BatchSize)
                .ToListAsync(cancellationToken)
                .ConfigureAwait(false);

            if (batchIds.Count == 0)
            {
                break;
            }

            totalDeleted += await dbContext.WatchEvents()
                .Where(w => batchIds.Contains(w.WATCH_EVENT_ID))
                .ExecuteDeleteAsync(cancellationToken)
                .ConfigureAwait(false);

            if (batchIds.Count < BatchSize)
            {
                break;
            }
        }

        return totalDeleted;
    }

    public async Task<IReadOnlyList<StudentCourseProgressRecord>> GetStudentProgressByCoursesAsync(
        IEnumerable<Guid> courseIds,
        int limit,
        CancellationToken cancellationToken)
    {
        var idList = courseIds.Distinct().ToList();
        if (idList.Count == 0)
        {
            return [];
        }

        var effectiveLimit = limit <= 0 ? 50 : Math.Min(limit, 100);

        // Real signals only: "last active" is the latest of the enrollment's last-accessed stamp and any
        // episode-progress heartbeat (null when the learner never opened the course — NOT the enrollment or
        // completion date), and completed episodes are counted from episode-progress rows rather than
        // back-computed from the percentage.
        var rows = await dbContext.Enrollments()
            .AsNoTracking()
            .Where(e => idList.Contains(e.COURSE_ID))
            .OrderByDescending(e => e.ENROLLED_AT_UTC)
            .Take(effectiveLimit)
            .Select(e => new
            {
                e.ENROLLMENT_ID,
                e.USER_ID,
                e.COURSE_ID,
                e.PROGRESS_PERCENT,
                e.LAST_ACCESSED_AT_UTC,
                LastProgressAtUtc = dbContext.EpisodeProgresses()
                    .Where(p => p.ENROLLMENT_ID == e.ENROLLMENT_ID)
                    .Max(p => (DateTime?)p.UPDATED_AT_UTC),
                CompletedEpisodes = dbContext.EpisodeProgresses()
                    .Count(p => p.ENROLLMENT_ID == e.ENROLLMENT_ID && p.IS_COMPLETED),
            })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .Select(r => new StudentCourseProgressRecord(
                r.ENROLLMENT_ID,
                r.USER_ID,
                r.COURSE_ID,
                r.PROGRESS_PERCENT,
                LatestOf(r.LAST_ACCESSED_AT_UTC, r.LastProgressAtUtc),
                r.CompletedEpisodes))
            .ToList();
    }

    private static DateTime? LatestOf(DateTime? first, DateTime? second) =>
        first is null ? second : second is null ? first : first > second ? first : second;
}
