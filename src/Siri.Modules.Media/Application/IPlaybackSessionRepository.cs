using Siri.Modules.Media.Domain;

namespace Siri.Modules.Media.Application;

public sealed record PlaybackUserActivity(
    Guid UserId,
    int SessionCount,
    int DistinctIpCount,
    string? LastIpAddress,
    IReadOnlyList<string> IpAddresses);

/// <summary>Persistence port for <see cref="PLAYBACK_SESSION"/> — see <see cref="IMediaAssetRepository"/>'s
/// own doc comment for why this lives here. Includes query methods for anomaly detection job (P2-06).</summary>
public interface IPlaybackSessionRepository
{
    Task<PLAYBACK_SESSION?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<(IReadOnlyList<PLAYBACK_SESSION> Items, int TotalCount)> GetPagedByUserIdAsync(
        Guid userId, int page, int pageSize, CancellationToken cancellationToken);

    Task<IReadOnlyList<PlaybackUserActivity>> GetUserActivitySinceAsync(
        DateTime sinceUtc, CancellationToken cancellationToken);

    void Add(PLAYBACK_SESSION playbackSession);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
