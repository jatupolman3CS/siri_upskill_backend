using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Application;

public interface ICartRepository
{
    /// <summary>The natural lookup for this aggregate — <see cref="CART.USER_ID"/> is unique (one cart
    /// per user), so there is no separate "GetById" the way <see cref="IOrderRepository"/> has one.</summary>
    Task<CART?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task AddAsync(CART cart, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
