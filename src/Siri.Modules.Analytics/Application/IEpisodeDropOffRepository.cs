using Siri.Modules.Analytics.Domain;

namespace Siri.Modules.Analytics.Application;

/// <summary>
/// Read-mostly repository over <see cref="EPISODE_DROP_OFF"/> rows — see
/// <see cref="IDailyCourseStatRepository"/>'s own doc comment for the full "why thin, why read-mostly, why
/// no pagination" reasoning (docs/DECISIONS.md D-17); it applies here identically, not repeated per
/// interface.
/// </summary>
public interface IEpisodeDropOffRepository
{
    /// <summary>The single row for one episode on one day, or <c>null</c> if the nightly job has not
    /// produced one yet — see <see cref="IDailyCourseStatRepository.GetAsync"/>'s doc comment; the same
    /// reasoning applies here.</summary>
    Task<EPISODE_DROP_OFF?> GetAsync(DateOnly date, Guid episodeId, CancellationToken cancellationToken);

    /// <summary>Every rollup row for one episode across a date range (inclusive on both ends), ordered by
    /// <see cref="EPISODE_DROP_OFF.DATE"/> ascending — the shape a per-episode drop-off/retention chart
    /// will need.</summary>
    Task<IReadOnlyList<EPISODE_DROP_OFF>> GetForEpisodeAsync(Guid episodeId, DateOnly fromDate, DateOnly toDate, CancellationToken cancellationToken);
}
