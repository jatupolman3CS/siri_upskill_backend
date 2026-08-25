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

                // Progress percentage is normalized: if avgSeconds > 0, calculate estimated watch %
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

    public async Task<int> PurgeOldWatchEventsAsync(DateTime olderThanUtc, CancellationToken cancellationToken)
    {
        var oldEvents = await dbContext.WatchEvents()
            .Where(w => w.OCCURRED_AT_UTC < olderThanUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        if (oldEvents.Count > 0)
        {
            dbContext.WatchEvents().RemoveRange(oldEvents);
            return await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return 0;
    }
}
