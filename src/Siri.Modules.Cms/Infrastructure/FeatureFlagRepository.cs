using Microsoft.EntityFrameworkCore;
using Siri.Modules.Cms.Application;
using Siri.Modules.Cms.Domain;
using Siri.Persistence;

namespace Siri.Modules.Cms.Infrastructure;

public sealed class FeatureFlagRepository(AppDbContext dbContext) : IFeatureFlagRepository
{
    public async Task<FEATURE_FLAG?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        await dbContext.FeatureFlags()
            .FirstOrDefaultAsync(f => f.FEATURE_FLAG_ID == id, cancellationToken)
            .ConfigureAwait(false);

    public async Task<FEATURE_FLAG?> GetByKeyAsync(string key, CancellationToken cancellationToken) =>
        await dbContext.FeatureFlags()
            .FirstOrDefaultAsync(f => f.KEY == key.Trim().ToLowerInvariant(), cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<FEATURE_FLAG>> GetAllAsync(CancellationToken cancellationToken) =>
        await dbContext.FeatureFlags()
            .AsNoTracking()
            .OrderBy(f => f.KEY)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<FEATURE_FLAG>> GetEnabledAsync(CancellationToken cancellationToken) =>
        await dbContext.FeatureFlags()
            .AsNoTracking()
            .Where(f => f.IS_ENABLED)
            .OrderBy(f => f.KEY)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public void Add(FEATURE_FLAG flag) => dbContext.FeatureFlags().Add(flag);

    public void Remove(FEATURE_FLAG flag) => dbContext.FeatureFlags().Remove(flag);

    public Task<int> SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
