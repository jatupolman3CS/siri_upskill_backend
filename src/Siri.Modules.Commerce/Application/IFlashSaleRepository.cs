using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Application;

public interface IFlashSaleRepository
{
    /// <summary>Includes <see cref="FLASH_SALE.FLASH_SALE_ITEMS"/> — same "aggregate loads its own
    /// children" shape <see cref="IBundleRepository.GetByIdAsync"/>'s own doc comment describes for
    /// <c>BUNDLE.BUNDLE_ITEMS</c>.</summary>
    Task<FLASH_SALE?> GetByIdAsync(Guid flashSaleId, CancellationToken cancellationToken);

    Task AddAsync(FLASH_SALE flashSale, CancellationToken cancellationToken);

    /// <summary>Public storefront listing — left as a stub; same "which window counts as current/upcoming"
    /// deferred-filtering reasoning as <see cref="IBundleRepository.ListAsync"/>'s own doc comment.</summary>
    Task<IReadOnlyList<FLASH_SALE>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<IReadOnlyList<FLASH_SALE>> GetActiveFlashSalesAsync(DateTime nowUtc, CancellationToken cancellationToken);

    Task<int> CountAsync(CancellationToken cancellationToken);
}
