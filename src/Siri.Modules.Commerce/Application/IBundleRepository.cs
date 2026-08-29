using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Application;

public interface IBundleRepository
{
    /// <summary>Includes <see cref="BUNDLE.BUNDLE_ITEMS"/> — a bundle detail view needs to show every
    /// course inside it, same "aggregate loads its own children" shape <c>CartRepository.GetByUserIdAsync</c>
    /// already uses for <c>CART.CART_ITEMS</c>.</summary>
    Task<BUNDLE?> GetByIdAsync(Guid bundleId, CancellationToken cancellationToken);

    Task AddAsync(BUNDLE bundle, CancellationToken cancellationToken);

    /// <summary>Public storefront listing — left as a stub; whether this excludes inactive/not-yet-started/
    /// already-ended bundles by default is a product decision this scaffold pass should not guess at (same
    /// reasoning <c>IOrderRepository.GetActiveByUserIdAsync</c>'s own doc comment gives for "active").</summary>
    Task<IReadOnlyList<BUNDLE>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<IReadOnlyList<BUNDLE>> GetActiveBundlesAsync(DateTime nowUtc, CancellationToken cancellationToken);

    Task<IReadOnlyList<BUNDLE>> GetBundlesByCourseIdAsync(Guid courseId, CancellationToken cancellationToken);

    Task<int> CountAsync(CancellationToken cancellationToken);
}
