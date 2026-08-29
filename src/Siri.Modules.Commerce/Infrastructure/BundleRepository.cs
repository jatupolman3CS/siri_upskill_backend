using Microsoft.EntityFrameworkCore;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Persistence;

namespace Siri.Modules.Commerce.Infrastructure;

public sealed class BundleRepository(AppDbContext dbContext) : IBundleRepository
{
    public Task<BUNDLE?> GetByIdAsync(Guid bundleId, CancellationToken cancellationToken) =>
        dbContext.Bundles().Include(b => b.BUNDLE_ITEMS).FirstOrDefaultAsync(b => b.BUNDLE_ID == bundleId, cancellationToken);

    public async Task AddAsync(BUNDLE bundle, CancellationToken cancellationToken)
    {
        dbContext.Bundles().Add(bundle);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BUNDLE>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        return await dbContext.Bundles()
            .Include(b => b.BUNDLE_ITEMS)
            .OrderBy(b => b.TITLE)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BUNDLE>> GetActiveBundlesAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        return await dbContext.Bundles()
            .Include(b => b.BUNDLE_ITEMS)
            .Where(b => b.IS_ACTIVE && (!b.STARTS_AT_UTC.HasValue || b.STARTS_AT_UTC <= nowUtc) && (!b.ENDS_AT_UTC.HasValue || b.ENDS_AT_UTC >= nowUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<BUNDLE>> GetBundlesByCourseIdAsync(Guid courseId, CancellationToken cancellationToken)
    {
        return await dbContext.Bundles()
            .Include(b => b.BUNDLE_ITEMS)
            .Where(b => b.IS_ACTIVE && b.BUNDLE_ITEMS.Any(i => i.COURSE_ID == courseId))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<int> CountAsync(CancellationToken cancellationToken) =>
        dbContext.Bundles().CountAsync(cancellationToken);
}
