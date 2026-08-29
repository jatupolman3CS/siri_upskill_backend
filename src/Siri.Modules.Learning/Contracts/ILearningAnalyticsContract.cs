namespace Siri.Modules.Learning.Contracts;

public sealed record EpisodeDropOffItem(Guid EpisodeId, int StartCount, int CompleteCount, decimal AvgWatchPercent);

public sealed record DailyCourseActivityItem(Guid CourseId, int Views, int Enrollments, decimal CompletionRate);

public sealed record StudentCourseProgressRecord(Guid EnrollmentId, Guid UserId, Guid CourseId, decimal ProgressPercent, DateTime? LastActiveAtUtc);

public interface ILearningAnalyticsContract
{
    Task<IReadOnlyList<EpisodeDropOffItem>> GetEpisodeDropOffRollupAsync(DateOnly date, CancellationToken cancellationToken);
    Task<IReadOnlyList<DailyCourseActivityItem>> GetDailyCourseActivityAsync(DateOnly date, CancellationToken cancellationToken);
    Task<int> PurgeOldWatchEventsAsync(DateTime olderThanUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<StudentCourseProgressRecord>> GetStudentProgressByCoursesAsync(
        IEnumerable<Guid> courseIds,
        int limit,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<StudentCourseProgressRecord>>(Array.Empty<StudentCourseProgressRecord>());
}
