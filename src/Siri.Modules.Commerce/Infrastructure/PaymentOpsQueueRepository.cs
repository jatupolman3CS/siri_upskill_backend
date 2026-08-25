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
}
