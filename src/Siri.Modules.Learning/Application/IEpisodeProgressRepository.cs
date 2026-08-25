using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// Data access for <see cref="EPISODE_PROGRESS"/>, consumed by <see cref="EpisodeProgressService"/>.
/// Deliberately narrower than <see cref="IEnrollmentRepository"/>/<see cref="ICertificateRepository"/> — no
/// generic <c>Query()</c> — matching this cluster's own "simpler, no separate create/delete actions" scope
/// (see <see cref="EpisodeProgressService"/>'s own doc comment): every real caller in this scaffold pass'
/// scope needs one of exactly these two lookups, so nothing wider is exposed yet.
/// </summary>
public interface IEpisodeProgressRepository
{
    /// <summary>Tracked lookup by the unique (EnrollmentId, EpisodeId) pair — the upsert check
    /// <c>EpisodeProgressService.UpsertProgressAsync</c> needs to decide between
    /// <see cref="EPISODE_PROGRESS.Create"/> and <see cref="EPISODE_PROGRESS.Touch"/>.</summary>
    Task<EPISODE_PROGRESS?> GetByEnrollmentAndEpisodeAsync(Guid enrollmentId, Guid episodeId, CancellationToken cancellationToken);

    /// <summary>Every progress row for one enrollment, untracked — backs the "resume where you left off"
    /// / course-progress-overview read (docs/DATABASE.md's day-one <c>IX_EpisodeProgress_Resume</c> index,
    /// see <c>EpisodeProgressConfiguration</c>).</summary>
    Task<IReadOnlyList<EPISODE_PROGRESS>> ListForEnrollmentAsync(Guid enrollmentId, CancellationToken cancellationToken);

    /// <summary>Stages a new row for insertion — does not persist until <see cref="SaveChangesAsync"/>.
    /// </summary>
    void Add(EPISODE_PROGRESS episodeProgress);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
