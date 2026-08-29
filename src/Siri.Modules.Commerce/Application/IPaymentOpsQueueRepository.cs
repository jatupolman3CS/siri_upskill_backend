using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Application;

public interface IPaymentOpsQueueRepository
{
    Task<PAYMENT_OPS_QUEUE?> GetByIdAsync(Guid paymentOpsQueueId, CancellationToken cancellationToken);

    Task AddAsync(PAYMENT_OPS_QUEUE entry, CancellationToken cancellationToken);

    Task<IReadOnlyList<PAYMENT_OPS_QUEUE>> GetOpenEntriesAsync(CancellationToken cancellationToken);

    Task<(IReadOnlyList<PAYMENT_OPS_QUEUE> Items, int TotalCount)> ListAsync(
        PaymentOpsQueueStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
