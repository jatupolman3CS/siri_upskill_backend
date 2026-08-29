using Microsoft.EntityFrameworkCore;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Persistence;

namespace Siri.Modules.Commerce.Infrastructure;

public sealed class PaymentOpsQueueRepository(AppDbContext dbContext) : IPaymentOpsQueueRepository
{
    public Task<PAYMENT_OPS_QUEUE?> GetByIdAsync(Guid paymentOpsQueueId, CancellationToken cancellationToken) =>
        dbContext.PaymentOpsQueue().FirstOrDefaultAsync(q => q.PAYMENT_OPS_QUEUE_ID == paymentOpsQueueId, cancellationToken);

    public async Task AddAsync(PAYMENT_OPS_QUEUE entry, CancellationToken cancellationToken)
    {
        dbContext.PaymentOpsQueue().Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PAYMENT_OPS_QUEUE>> GetOpenEntriesAsync(CancellationToken cancellationToken)
    {
        return await dbContext.PaymentOpsQueue()
            .Where(q => q.STATUS == PaymentOpsQueueStatus.Open)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<(IReadOnlyList<PAYMENT_OPS_QUEUE> Items, int TotalCount)> ListAsync(
        PaymentOpsQueueStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var effectivePage = page < 1 ? 1 : page;
        var effectivePageSize = pageSize < 1 ? 20 : Math.Min(pageSize, 100);

        var query = dbContext.PaymentOpsQueue().AsQueryable();

        if (status.HasValue)
        {
            query = query.Where(q => q.STATUS == status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .OrderByDescending(q => q.PAYMENT_OPS_QUEUE_ID)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (items, totalCount);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
