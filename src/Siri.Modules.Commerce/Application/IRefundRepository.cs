using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Application;

public interface IRefundRepository
{
    Task<REFUND?> GetByIdAsync(Guid refundId, CancellationToken cancellationToken);

    Task AddAsync(REFUND refund, CancellationToken cancellationToken);

    Task<IReadOnlyList<REFUND>> GetPendingAsync(CancellationToken cancellationToken);

    Task<(IReadOnlyList<REFUND> Items, int TotalCount)> ListPendingPagedAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
