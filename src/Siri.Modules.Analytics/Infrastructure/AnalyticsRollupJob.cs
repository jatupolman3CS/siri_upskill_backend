using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Siri.Modules.Analytics.Domain;
using Siri.Modules.Commerce.Contracts;
using Siri.Modules.Learning.Contracts;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Analytics.Infrastructure;

/// <summary>
/// Nightly Hangfire recurring job that computes aggregated analytics from watch events,
/// enrollments, and orders into DAILY_COURSE_STATS and EPISODE_DROP_OFFS tables,
/// and purges raw watch events older than 90 days (P2-32).
/// </summary>
public sealed class AnalyticsRollupJob(
    AppDbContext dbContext,
    ILearningAnalyticsContract learningAnalyticsContract,
    ICommerceStatsContract commerceStatsContract,
    IClock clock,
    ILogger<AnalyticsRollupJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 300)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        var targetDate = DateOnly.FromDateTime(clock.UtcNow.AddDays(-1));
        await RunForDateAsync(targetDate, cancellationToken).ConfigureAwait(false);
    }

    public async Task RunForDateAsync(DateOnly targetDate, CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting Analytics Rollup for date: {TargetDate}", targetDate);

        try
        {
            // 1. Episode Drop-off rollup
            var dropOffItems = await learningAnalyticsContract.GetEpisodeDropOffRollupAsync(targetDate, cancellationToken).ConfigureAwait(false);

            var existingDropOffs = await dbContext.Set<EPISODE_DROP_OFF>()
                .Where(d => d.DATE == targetDate)
                .ToDictionaryAsync(d => d.EPISODE_ID, cancellationToken)
                .ConfigureAwait(false);

            foreach (var item in dropOffItems)
            {
                if (existingDropOffs.TryGetValue(item.EpisodeId, out var existing))
                {
                    existing.ApplyRollup(item.StartCount, item.CompleteCount, item.AvgWatchPercent);
                }
                else
                {
                    var newRecord = EPISODE_DROP_OFF.Create(targetDate, item.EpisodeId);
                    newRecord.ApplyRollup(item.StartCount, item.CompleteCount, item.AvgWatchPercent);
                    dbContext.Set<EPISODE_DROP_OFF>().Add(newRecord);
                }
            }

            // 2. Daily Course Stats rollup (Learning activity + Commerce revenue)
            var courseActivities = await learningAnalyticsContract.GetDailyCourseActivityAsync(targetDate, cancellationToken).ConfigureAwait(false);
            var courseRevenues = await commerceStatsContract.GetDailyCourseRevenueAsync(targetDate, cancellationToken).ConfigureAwait(false);

            var courseIds = courseActivities.Select(a => a.CourseId)
                .Union(courseRevenues.Keys)
                .Distinct()
                .ToList();

            var existingStats = await dbContext.Set<DAILY_COURSE_STAT>()
                .Where(s => s.DATE == targetDate)
                .ToDictionaryAsync(s => s.COURSE_ID, cancellationToken)
                .ConfigureAwait(false);

            var activityMap = courseActivities.ToDictionary(a => a.CourseId);

            foreach (var courseId in courseIds)
            {
                activityMap.TryGetValue(courseId, out var activity);
                courseRevenues.TryGetValue(courseId, out var revenue);

                var views = activity?.Views ?? 0;
                var enrollments = activity?.Enrollments ?? 0;
                var completionRate = activity?.CompletionRate ?? 0m;

                if (existingStats.TryGetValue(courseId, out var existing))
                {
                    existing.ApplyRollup(views, enrollments, revenue, completionRate);
                }
                else
                {
                    var newStat = DAILY_COURSE_STAT.Create(targetDate, courseId);
                    newStat.ApplyRollup(views, enrollments, revenue, completionRate);
                    dbContext.Set<DAILY_COURSE_STAT>().Add(newStat);
                }
            }

            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // 3. Purge raw watch events older than 90 days (P2-32)
            var purgeThreshold = clock.UtcNow.AddDays(-90);
            var purgedCount = await learningAnalyticsContract.PurgeOldWatchEventsAsync(purgeThreshold, cancellationToken).ConfigureAwait(false);

            logger.LogInformation(
                "Completed Analytics Rollup for date: {TargetDate}. DropOffs: {DropOffCount}, CourseStats: {StatsCount}, PurgedWatchEvents: {PurgedCount}",
                targetDate, dropOffItems.Count, courseIds.Count, purgedCount);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error occurred during Analytics Rollup for date: {TargetDate}", targetDate);
            throw;
        }
    }
}
