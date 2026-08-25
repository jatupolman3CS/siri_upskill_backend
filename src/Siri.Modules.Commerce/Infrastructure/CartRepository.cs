using Microsoft.EntityFrameworkCore;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Persistence;

namespace Siri.Modules.Commerce.Infrastructure;

public sealed class CartRepository(AppDbContext dbContext) : ICartRepository
{
    public Task<CART?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.Carts().Include(c => c.CART_ITEMS).FirstOrDefaultAsync(c => c.USER_ID == userId, cancellationToken);

    public async Task AddAsync(CART cart, CancellationToken cancellationToken)
    {
        dbContext.Carts().Add(cart);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
