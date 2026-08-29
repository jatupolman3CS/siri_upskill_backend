using Siri.Modules.Payout.Domain;

namespace Siri.Modules.Payout.Application;

/// <summary>
/// Data access for <see cref="PAYOUT_BATCH_ITEM"/>, consumed internally by <see cref="PayoutBatchService"/> only.
/// </summary>
public interface IPayoutBatchItemRepository
{
    Task<PAYOUT_BATCH_ITEM?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Untracked (<c>AsNoTracking</c>) — every item belonging to one batch.</summary>
    Task<IReadOnlyList<PAYOUT_BATCH_ITEM>> GetByBatchIdAsync(Guid batchId, CancellationToken cancellationToken);

    /// <summary>Untracked (<c>AsNoTracking</c>) query source.</summary>
    IQueryable<PAYOUT_BATCH_ITEM> Query();

    /// <summary>Stages new rows for insertion.</summary>
    void AddRange(IEnumerable<PAYOUT_BATCH_ITEM> items);
}
