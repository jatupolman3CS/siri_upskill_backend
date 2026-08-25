using Microsoft.EntityFrameworkCore;
using Siri.Modules.Payout.Application;
using Siri.Modules.Payout.Domain;
using Siri.Persistence;

namespace Siri.Modules.Payout.Infrastructure;

public sealed class PayoutBatchRepository(AppDbContext dbContext) : IPayoutBatchRepository
{
    public Task<PAYOUT_BATCH?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.PayoutBatches()
            .Include(b => b.Items)
            .FirstOrDefaultAsync(b => b.PAYOUT_BATCH_ID == id, cancellationToken);

    public IQueryable<PAYOUT_BATCH> Query() => dbContext.PayoutBatches().AsNoTracking();

    public async Task<IReadOnlyList<InstructorPayoutHistoryItem>> GetPayoutHistoryForInstructorAsync(
        Guid instructorId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var query = from item in dbContext.PayoutBatchItems().AsNoTracking()
                    join batch in dbContext.PayoutBatches().AsNoTracking() on item.BATCH_ID equals batch.PAYOUT_BATCH_ID
                    where item.INSTRUCTOR_ID == instructorId
                    orderby batch.CreatedAtUtc descending
                    select new InstructorPayoutHistoryItem(
                        item.PAYOUT_BATCH_ITEM_ID,
                        batch.PERIOD_KEY,
                        item.AMOUNT,
                        item.NET_AMOUNT,
                        item.WITHHOLDING_TAX_AMOUNT,
                        batch.EXECUTED_AT_UTC,
                        item.STATUS.ToString(),
                        batch.EXECUTED_AT_UTC);

        return await query
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<int> CountPayoutHistoryForInstructorAsync(Guid instructorId, CancellationToken cancellationToken)
    {
        return await dbContext.PayoutBatchItems()
            .AsNoTracking()
            .Where(i => i.INSTRUCTOR_ID == instructorId)
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public void Add(PAYOUT_BATCH batch) => dbContext.PayoutBatches().Add(batch);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
