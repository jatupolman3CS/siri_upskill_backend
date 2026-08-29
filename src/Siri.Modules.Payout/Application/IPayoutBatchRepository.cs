using Siri.Modules.Payout.Domain;

namespace Siri.Modules.Payout.Application;

/// <summary>
/// Data access for <see cref="PAYOUT_BATCH"/>, consumed by <see cref="PayoutBatchService"/>.
/// </summary>
public interface IPayoutBatchRepository
{
    /// <summary>Tracked lookup by primary key, including <see cref="PAYOUT_BATCH.Items"/>.</summary>
    Task<PAYOUT_BATCH?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Tracked lookup by period key.</summary>
    Task<PAYOUT_BATCH?> GetByPeriodKeyAsync(string periodKey, CancellationToken cancellationToken);

    /// <summary>Untracked (<c>AsNoTracking</c>) query source for the admin batch list.</summary>
    IQueryable<PAYOUT_BATCH> Query();

    Task<IReadOnlyList<InstructorPayoutHistoryItem>> GetPayoutHistoryForInstructorAsync(Guid instructorId, int page, int pageSize, CancellationToken cancellationToken);

    Task<int> CountPayoutHistoryForInstructorAsync(Guid instructorId, CancellationToken cancellationToken);

    /// <summary>Stages a new row for insertion — does not persist until <see cref="SaveChangesAsync"/>.</summary>
    void Add(PAYOUT_BATCH batch);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
