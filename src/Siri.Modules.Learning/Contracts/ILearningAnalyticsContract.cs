namespace Siri.Modules.Learning.Contracts;

public sealed record EpisodeDropOffItem(Guid EpisodeId, int StartCount, int CompleteCount, decimal AvgWatchPercent);

public sealed record DailyCourseActivityItem(Guid CourseId, int Views, int Enrollments, decimal CompletionRate);

/// <param name="LastActiveAtUtc">The learner's real last activity on this enrollment (latest of the enrollment's
/// last-accessed stamp and any episode-progress heartbeat), or <c>null</c> when they never opened it — never
/// substituted with the enrollment/completion date.</param>
/// <param name="CompletedEpisodes">Number of episodes this enrollment has actually finished (counted from episode
/// progress rows), not derived from <paramref name="ProgressPercent"/>.</param>
public sealed record StudentCourseProgressRecord(
    Guid EnrollmentId,
    Guid UserId,
    Guid CourseId,
    decimal ProgressPercent,
    DateTime? LastActiveAtUtc,
    int CompletedEpisodes);

/// <summary>
/// <paramref name="ProgressRows"/> episode-progress rows of <paramref name="EpisodeId"/> that each report exactly
/// <paramref name="WatchedSeconds"/> watched seconds. Grouping by value (rather than one row per learner) keeps
/// the result small while still letting the caller cap every individual row at the episode's real duration.
/// </summary>
public sealed record EpisodeWatchTimeBucket(Guid EpisodeId, int WatchedSeconds, int ProgressRows);

public interface ILearningAnalyticsContract
{
    Task<IReadOnlyList<EpisodeDropOffItem>> GetEpisodeDropOffRollupAsync(DateOnly date, CancellationToken cancellationToken);
    Task<IReadOnlyList<DailyCourseActivityItem>> GetDailyCourseActivityAsync(DateOnly date, CancellationToken cancellationToken);
    Task<int> PurgeOldWatchEventsAsync(DateTime olderThanUtc, CancellationToken cancellationToken);
    Task<IReadOnlyList<StudentCourseProgressRecord>> GetStudentProgressByCoursesAsync(
        IEnumerable<Guid> courseIds,
        int limit,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<StudentCourseProgressRecord>>(Array.Empty<StudentCourseProgressRecord>());

    /// <summary>
    /// Real watch-time data for the episodes of <paramref name="courseIds"/>: the recorded watched seconds
    /// (<c>EPISODE_PROGRESS.WATCHED_SECONDS</c>) of every progress row whose last activity is at or after
    /// <paramref name="activeSinceUtc"/>, grouped by (episode, watched seconds). Only rows with recorded watch
    /// time are returned; a course nobody has watched yields an empty list (genuinely zero minutes).
    /// <para>
    /// Note on scope: only the cumulative per-learner, per-episode total is stored (not per-day watch time), so
    /// the rows are selected by "last active in the window" — the cumulative seconds of those rows are returned,
    /// not just the seconds watched inside the window.
    /// </para>
    /// </summary>
    Task<IReadOnlyList<EpisodeWatchTimeBucket>> GetEpisodeWatchTimeBucketsAsync(
        IEnumerable<Guid> courseIds,
        DateTime activeSinceUtc,
        CancellationToken cancellationToken);

    /// <summary>
    /// How many different learners hold (or held) an enrollment in <paramref name="courseIds"/>, and how many of them enrolled at or after <paramref name="sinceUtc"/>
    /// (docs/contracts/P11-10-instructor-dashboard-summary.md section 2.3). A <c>Revoked</c> enrollment is not counted; <c>Active</c> and <c>Expired</c> are. A learner enrolled in
    /// several of the courses counts once. Default: zeros, so another implementer of this interface keeps compiling without opting in.
    /// </summary>
    Task<LearnerCounts> GetLearnerCountsAsync(
        IReadOnlyCollection<Guid> courseIds,
        DateTime sinceUtc,
        CancellationToken cancellationToken) => Task.FromResult(new LearnerCounts(0, 0));

    /// <summary>
    /// The sum of <c>EPISODE_PROGRESS.WATCHED_SECONDS</c> over progress rows of enrollments in <paramref name="courseIds"/> that were updated at or after
    /// <paramref name="sinceUtc"/> — measured watch time, never an estimate. As with <see cref="GetEpisodeWatchTimeBucketsAsync"/> the stored total is cumulative per learner and
    /// episode, so this is "the cumulative seconds of the rows that moved in the window". Default: 0.
    /// </summary>
    Task<long> GetWatchedSecondsAsync(
        IReadOnlyCollection<Guid> courseIds,
        DateTime sinceUtc,
        CancellationToken cancellationToken) => Task.FromResult(0L);
}

/// <param name="DistinctLearners">Different learners with an Active or Expired enrollment in the given courses.</param>
/// <param name="DistinctLearnersSince">The subset of them whose enrollment date is at or after the requested instant.</param>
public sealed record LearnerCounts(int DistinctLearners, int DistinctLearnersSince);
