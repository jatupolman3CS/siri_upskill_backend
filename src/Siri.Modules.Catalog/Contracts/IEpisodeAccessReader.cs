namespace Siri.Modules.Catalog.Contracts;

/// <summary>
/// Cross-module contract for checking whether a user has learning access (active enrollment)
/// to a specific episode without directly coupling Catalog to the Learning module's schema.
/// </summary>
public interface IEpisodeAccessReader
{
    Task<bool> CanUserAccessEpisodeAsync(Guid userId, Guid episodeId, CancellationToken cancellationToken);
}
