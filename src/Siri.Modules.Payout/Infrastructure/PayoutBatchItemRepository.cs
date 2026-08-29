using Microsoft.EntityFrameworkCore;
using Siri.Modules.Payout.Application;
using Siri.Modules.Payout.Domain;
using Siri.Persistence;

namespace Siri.Modules.Payout.Infrastructure;

public sealed class PayoutBatchItemRepository(AppDbContext dbContext) : IPayoutBatchItemRepository
{
    public Task<PAYOUT_BATCH_ITEM?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.PayoutBatchItems().FirstOrDefaultAsync(i => i.PAYOUT_BATCH_ITEM_ID == id, cancellationToken);

    public async Task<IReadOnlyList<PAYOUT_BATCH_ITEM>> GetByBatchIdAsync(Guid batchId, CancellationToken cancellationToken) =>
        await dbContext.PayoutBatchItems()
            .Where(i => i.BATCH_ID == batchId)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public IQueryable<PAYOUT_BATCH_ITEM> Query() => dbContext.PayoutBatchItems().AsNoTracking();

    public void AddRange(IEnumerable<PAYOUT_BATCH_ITEM> items) => dbContext.PayoutBatchItems().AddRange(items);
}
