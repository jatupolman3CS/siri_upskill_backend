using Siri.Modules.Cms.Domain;

namespace Siri.Modules.Cms.Application;

public interface IFeatureFlagRepository
{
    Task<FEATURE_FLAG?> GetByIdAsync(Guid id, CancellationToken cancellationToken);
    Task<FEATURE_FLAG?> GetByKeyAsync(string key, CancellationToken cancellationToken);
    Task<IReadOnlyList<FEATURE_FLAG>> GetAllAsync(CancellationToken cancellationToken);
    Task<IReadOnlyList<FEATURE_FLAG>> GetEnabledAsync(CancellationToken cancellationToken);
    void Add(FEATURE_FLAG flag);
    void Remove(FEATURE_FLAG flag);
    Task<int> SaveChangesAsync(CancellationToken cancellationToken);
}
