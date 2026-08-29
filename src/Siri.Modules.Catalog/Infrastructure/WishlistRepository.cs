using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Domain;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Infrastructure;

public sealed class WishlistRepository(AppDbContext dbContext) : IWishlistRepository
{
    public async Task<IReadOnlyList<WISHLIST_ITEM>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.Wishlists()
            .Where(w => w.UserId == userId)
            .OrderByDescending(w => w.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<WISHLIST_ITEM?> GetItemAsync(Guid userId, Guid courseId, CancellationToken cancellationToken) =>
        dbContext.Wishlists().FirstOrDefaultAsync(w => w.UserId == userId && w.CourseId == courseId, cancellationToken);

    public async Task AddAsync(WISHLIST_ITEM item, CancellationToken cancellationToken)
    {
        dbContext.Wishlists().Add(item);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task RemoveAsync(WISHLIST_ITEM item, CancellationToken cancellationToken)
    {
        dbContext.Wishlists().Remove(item);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
