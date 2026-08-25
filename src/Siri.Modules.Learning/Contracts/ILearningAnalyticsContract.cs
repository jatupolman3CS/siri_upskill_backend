namespace Siri.Modules.Learning.Contracts;

public sealed record EpisodeDropOffItem(Guid EpisodeId, int StartCount, int CompleteCount, decimal AvgWatchPercent);

public sealed record DailyCourseActivityItem(Guid CourseId, int Views, int Enrollments, decimal CompletionRate);

public interface ILearningAnalyticsContract
{
    Task<IReadOnlyList<EpisodeDropOffItem>> GetEpisodeDropOffRollupAsync(DateOnly date, CancellationToken cancellationToken);
    Task<IReadOnlyList<DailyCourseActivityItem>> GetDailyCourseActivityAsync(DateOnly date, CancellationToken cancellationToken);
    Task<int> PurgeOldWatchEventsAsync(DateTime olderThanUtc, CancellationToken cancellationToken);
}
