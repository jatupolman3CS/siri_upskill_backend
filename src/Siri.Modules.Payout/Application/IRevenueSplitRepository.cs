using Siri.Modules.Payout.Domain;

namespace Siri.Modules.Payout.Application;

/// <summary>
/// Data access for <see cref="REVENUE_SPLIT"/>, consumed by <see cref="RevenueSplitService"/>. Interface
/// name/members are NOT uppercased — D-17's UPPERCASE naming exception is entity classes/properties and
/// DB tables/columns only, not Repository/Service/DTO/interface names (see this module's
/// <c>REVENUE_SPLIT</c>'s own doc comment).
/// <para>
/// <see cref="SaveChangesAsync"/> lives here because <see cref="REVENUE_SPLIT"/> is an aggregate root with
/// no child entities of its own — unlike <see cref="IPayoutBatchItemRepository"/>, which deliberately has
/// no <c>SaveChangesAsync</c> of its own (see that interface's own doc comment for the shared-DbContext
/// reasoning).
/// </para>
/// </summary>
public interface IRevenueSplitRepository
{
    /// <summary>Tracked lookup by primary key — <c>null</c> if no such row exists.</summary>
    Task<REVENUE_SPLIT?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Tracked lookup by the unique <see cref="REVENUE_SPLIT.ORDER_ITEM_ID"/> — supports
    /// idempotent creation (has a split already been recorded for this order item?).</summary>
    Task<REVENUE_SPLIT?> GetByOrderItemIdAsync(Guid orderItemId, CancellationToken cancellationToken);

    /// <summary>Untracked (<c>AsNoTracking</c>) query source for read scenarios — the caller composes its
    /// own filtering/paging/projection (database.md: "Projection ไปเป็น DTO ตรง ๆ ดีกว่าดึง entity มาทั้งก้อน
    /// แล้ว map").</summary>
    IQueryable<REVENUE_SPLIT> Query();

    Task<decimal> GetTotalEarningsAsync(Guid instructorId, CancellationToken cancellationToken);

    Task<decimal> GetPendingEarningsAsync(Guid instructorId, CancellationToken cancellationToken);

    /// <summary>Stages a new row for insertion — does not persist until <see cref="SaveChangesAsync"/>.</summary>
    void Add(REVENUE_SPLIT revenueSplit);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
