using Microsoft.EntityFrameworkCore;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Persistence;

namespace Siri.Modules.Commerce.Infrastructure;

public sealed class FlashSaleRepository(AppDbContext dbContext) : IFlashSaleRepository
{
    public Task<FLASH_SALE?> GetByIdAsync(Guid flashSaleId, CancellationToken cancellationToken) =>
        dbContext.FlashSales().Include(f => f.FLASH_SALE_ITEMS).FirstOrDefaultAsync(f => f.FLASH_SALE_ID == flashSaleId, cancellationToken);

    public async Task AddAsync(FLASH_SALE flashSale, CancellationToken cancellationToken)
    {
        dbContext.FlashSales().Add(flashSale);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FLASH_SALE>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        return await dbContext.FlashSales()
            .Include(f => f.FLASH_SALE_ITEMS)
            .OrderByDescending(f => f.STARTS_AT_UTC)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<FLASH_SALE>> GetActiveFlashSalesAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        return await dbContext.FlashSales()
            .Include(f => f.FLASH_SALE_ITEMS)
            .Where(f => f.IS_ACTIVE && f.STARTS_AT_UTC <= nowUtc && f.ENDS_AT_UTC >= nowUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken) =>
        dbContext.FlashSales().CountAsync(cancellationToken);
}
