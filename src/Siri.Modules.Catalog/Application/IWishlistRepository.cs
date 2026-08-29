using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Application;

public interface IWishlistRepository
{
    Task<IReadOnlyList<WISHLIST_ITEM>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<WISHLIST_ITEM?> GetItemAsync(Guid userId, Guid courseId, CancellationToken cancellationToken);

    Task AddAsync(WISHLIST_ITEM item, CancellationToken cancellationToken);

    Task RemoveAsync(WISHLIST_ITEM item, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
