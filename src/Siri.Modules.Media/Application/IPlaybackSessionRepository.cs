using Siri.Modules.Media.Domain;

namespace Siri.Modules.Media.Application;

/// <summary>Persistence port for <see cref="PLAYBACK_SESSION"/> — see <see cref="IMediaAssetRepository"/>'s
/// own doc comment for why this lives here. No dedicated Service/Endpoints consume this in this scaffold
/// pass (docs/DECISIONS.md D-17: pure forensics log, not wired to any HTTP-facing feature yet) — kept ready
/// for whichever later task needs to look sessions up (e.g. an admin "investigate this leak" tool).</summary>
public interface IPlaybackSessionRepository
{
    Task<PLAYBACK_SESSION?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<(IReadOnlyList<PLAYBACK_SESSION> Items, int TotalCount)> GetPagedByUserIdAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken);

    void Add(PLAYBACK_SESSION playbackSession);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
