using Microsoft.EntityFrameworkCore;
using Siri.Modules.Cms.Application;
using Siri.Modules.Cms.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Cms.Infrastructure;

public sealed class BannerRepository(AppDbContext dbContext) : IBannerRepository
{
    public Task<BANNER?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Banners().FirstOrDefaultAsync(b => b.BANNER_ID == id, cancellationToken);

    public async Task<IReadOnlyList<BANNER>> GetByPlacementAsync(string placement, CancellationToken cancellationToken) =>
        await dbContext.Banners()
            .Where(b => b.PLACEMENT == placement)
            .OrderBy(b => b.SORT_ORDER)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<BANNER>> GetActiveByPlacementAsync(string placement, DateTime nowUtc, CancellationToken cancellationToken) =>
        await dbContext.Banners()
            .Where(b => b.PLACEMENT == placement
                        && b.IS_ACTIVE
                        && (b.STARTS_AT_UTC == null || b.STARTS_AT_UTC <= nowUtc)
                        && (b.ENDS_AT_UTC == null || b.ENDS_AT_UTC >= nowUtc))
            .OrderBy(b => b.SORT_ORDER)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<PagedResult<BANNER>> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var totalCount = await dbContext.Banners().CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await dbContext.Banners()
            .OrderBy(b => b.PLACEMENT)
            .ThenBy(b => b.SORT_ORDER)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return PagedResult<BANNER>.Create(items, totalCount, page, pageSize);
    }

    public void Add(BANNER banner) => dbContext.Banners().Add(banner);

    public void Remove(BANNER banner) => dbContext.Banners().Remove(banner);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
