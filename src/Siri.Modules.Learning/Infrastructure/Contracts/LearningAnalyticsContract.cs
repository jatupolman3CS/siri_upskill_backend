using Microsoft.EntityFrameworkCore;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Learning.Domain;
using Siri.Persistence;

namespace Siri.Modules.Learning.Infrastructure.Contracts;

public sealed class LearningAnalyticsContract(AppDbContext dbContext) : ILearningAnalyticsContract
{
    public async Task<IReadOnlyList<EpisodeDropOffItem>> GetEpisodeDropOffRollupAsync(DateOnly date, CancellationToken cancellationToken)
    {
        var startUtc = date.ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
        var endUtc = date.ToDateTime(TimeOnly.MaxValue, DateTimeKind.Utc);

        var events = await dbContext.WatchEvents()
            .AsNoTracking()
            .Where(w => w.OCCURRED_AT_UTC >= startUtc && w.OCCURRED_AT_UTC <= endUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (events.Count == 0)
        {
            return [];
        }

        var results = events
            .GroupBy(w => w.EPISODE_ID)
            .Select(g =>
            {
                var startCount = g.Count(w => w.EVENT_TYPE == WatchEventType.Play);
                var completeCount = g.Count(w => w.EVENT_TYPE == WatchEventType.Ended);
                var avgSeconds = g.Average(w => (double)w.POSITION_SECONDS);

                var avgWatchPercent = g.Any(w => w.EVENT_TYPE == WatchEventType.Ended)
                    ? Math.Min(100m, (decimal)(completeCount * 100.0 / Math.Max(1, startCount)))
                    : Math.Min(100m, (decimal)(avgSeconds > 0 ? 50.0 : 0.0));

                return new EpisodeDropOffItem(g.Key, startCount, completeCount, avgWatchPercent);
            })
            .ToList();

        return results;
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

        var enrollments = await dbContext.Enrollments()
            .AsNoTracking()
            .Where(e => idList.Contains(e.COURSE_ID))
            .OrderByDescending(e => e.ENROLLED_AT_UTC)
            .Take(effectiveLimit)
            .Select(e => new StudentCourseProgressRecord(
                e.ENROLLMENT_ID,
                e.USER_ID,
                e.COURSE_ID,
                e.PROGRESS_PERCENT,
                e.COMPLETED_AT_UTC ?? e.ENROLLED_AT_UTC))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return enrollments;
    }
}
