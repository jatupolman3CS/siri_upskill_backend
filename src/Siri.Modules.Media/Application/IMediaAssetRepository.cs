using Siri.Modules.Media.Domain;

namespace Siri.Modules.Media.Application;

/// <summary>
/// Persistence port for <see cref="MEDIA_ASSET"/>, implemented by
/// <c>Siri.Modules.Media.Infrastructure.MediaAssetRepository</c>. Lives here (not Infrastructure) so
/// <see cref="MediaAssetService"/> depends only on this abstraction — the D-17 Repository+Service pattern's
/// whole point: a later task's unit tests fake this interface + <see cref="Siri.SharedKernel.IClock"/>
/// directly, no database needed. Interface name is not uppercased — only entity classes/properties that map
/// 1:1 to a table/column follow the D-17 exception (see <see cref="MEDIA_ASSET"/>'s own doc comment).
/// </summary>
public interface IMediaAssetRepository
{
    Task<MEDIA_ASSET?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<MEDIA_ASSET?> GetByProviderAssetIdAsync(string providerAssetId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<MEDIA_ASSET> Items, int TotalCount)> GetPagedByUploaderAsync(
        Guid uploadedByUserId, int page, int pageSize, CancellationToken cancellationToken);

    void Add(MEDIA_ASSET mediaAsset);

    void Remove(MEDIA_ASSET mediaAsset);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
