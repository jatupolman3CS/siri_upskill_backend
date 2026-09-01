using Siri.Modules.Payout.Domain;

namespace Siri.Modules.Payout.Application;

/// <summary>
/// Data access for <see cref="REVENUE_SPLIT"/>, consumed by <see cref="RevenueSplitService"/>.
/// </summary>
public interface IRevenueSplitRepository
{
    /// <summary>Tracked lookup by primary key — <c>null</c> if no such row exists.</summary>
    Task<REVENUE_SPLIT?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Tracked lookup by the unique <see cref="REVENUE_SPLIT.ORDER_ITEM_ID"/> — supports
    /// idempotent creation (has a split already been recorded for this order item?).</summary>
    Task<REVENUE_SPLIT?> GetByOrderItemIdAsync(Guid orderItemId, CancellationToken cancellationToken);

    /// <summary>Tracked lookup of all splits matching the given order item IDs (for refund reversal).</summary>
    Task<IReadOnlyList<REVENUE_SPLIT>> GetByOrderItemIdsAsync(IEnumerable<Guid> orderItemIds, CancellationToken cancellationToken);

    /// <summary>Tracked lookup of all pending/payable splits eligible for payout (created on or before <paramref name="holdCutOffUtc"/>).</summary>
    Task<IReadOnlyList<REVENUE_SPLIT>> GetEligibleSplitsForPayoutAsync(DateTime holdCutOffUtc, CancellationToken cancellationToken);

    /// <summary>Tracked lookup of splits linked to a specific payout batch item.</summary>
    Task<IReadOnlyList<REVENUE_SPLIT>> GetSplitsByBatchItemIdAsync(Guid batchItemId, CancellationToken cancellationToken);

    /// <summary>
    /// Batched form of <see cref="GetSplitsByBatchItemIdAsync"/> — tracked lookup of every split linked to
    /// any of <paramref name="batchItemIds"/> in one query, consumed by
    /// <c>PayoutBatchService.ExecuteBatchAsync</c> to mark all of a batch's splits Paid without looping
    /// the single-item lookup once per <c>PAYOUT_BATCH_ITEM</c>. Default implementation loops
    /// <see cref="GetSplitsByBatchItemIdAsync"/> (functionally correct, not batched) so existing test
    /// doubles for this interface keep compiling/behaving correctly without changes — only
    /// <c>Infrastructure.RevenueSplitRepository</c> overrides this with a real single-query implementation.
    /// </summary>
    async Task<IReadOnlyList<REVENUE_SPLIT>> GetSplitsByBatchItemIdsAsync(IReadOnlyCollection<Guid> batchItemIds, CancellationToken cancellationToken)
    {
        var results = new List<REVENUE_SPLIT>();
        foreach (var batchItemId in batchItemIds)
        {
            results.AddRange(await GetSplitsByBatchItemIdAsync(batchItemId, cancellationToken).ConfigureAwait(false));
        }

        return results;
    }

    /// <summary>Untracked (<c>AsNoTracking</c>) query source for read scenarios.</summary>
    IQueryable<REVENUE_SPLIT> Query();

    Task<decimal> GetTotalEarningsAsync(Guid instructorId, CancellationToken cancellationToken);

    Task<decimal> GetPendingEarningsAsync(Guid instructorId, CancellationToken cancellationToken);

    /// <summary>Stages a new row for insertion — does not persist until <see cref="SaveChangesAsync"/>.</summary>
    void Add(REVENUE_SPLIT revenueSplit);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
