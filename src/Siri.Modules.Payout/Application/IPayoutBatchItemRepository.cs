using Siri.Modules.Payout.Domain;

namespace Siri.Modules.Payout.Application;

/// <summary>
/// Data access for <see cref="PAYOUT_BATCH_ITEM"/>, consumed internally by <see cref="PayoutBatchService"/>
/// only — this entity has no dedicated Service/Endpoints of its own (see <see cref="PAYOUT_BATCH_ITEM"/>'s
/// own doc comment). Interface name/members are NOT uppercased — see
/// <see cref="IRevenueSplitRepository"/>'s own doc comment for the naming-exception reasoning.
/// <para>
/// Deliberately has no <c>SaveChangesAsync</c> of its own, unlike the three aggregate-root repositories in
/// this module: every repository in <c>Siri.Modules.Payout.Infrastructure</c> shares the same scoped
/// <see cref="Siri.Persistence.AppDbContext"/> instance per request, so calling
/// <c>IPayoutBatchRepository.SaveChangesAsync</c> persists any pending changes staged through this
/// repository too (a single unit-of-work over one <c>DbContext</c> instance) — <see cref="PAYOUT_BATCH_ITEM"/>
/// rows are only ever written together with their owning <see cref="PAYOUT_BATCH"/>, never on their own.
/// </para>
/// </summary>
public interface IPayoutBatchItemRepository
{
    /// <summary>Untracked (<c>AsNoTracking</c>) — every item belonging to one batch.</summary>
    Task<IReadOnlyList<PAYOUT_BATCH_ITEM>> GetByBatchIdAsync(Guid batchId, CancellationToken cancellationToken);

    /// <summary>Untracked (<c>AsNoTracking</c>) query source.</summary>
    IQueryable<PAYOUT_BATCH_ITEM> Query();

    /// <summary>Stages new rows for insertion — does not persist until the owning
    /// <see cref="PAYOUT_BATCH"/>'s repository calls <c>SaveChangesAsync</c> (see this interface's own doc
    /// comment).</summary>
    void AddRange(IEnumerable<PAYOUT_BATCH_ITEM> items);
}
