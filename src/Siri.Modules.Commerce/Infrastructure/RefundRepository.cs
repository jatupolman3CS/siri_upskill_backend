using Microsoft.EntityFrameworkCore;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Persistence;

namespace Siri.Modules.Commerce.Infrastructure;

public sealed class RefundRepository(AppDbContext dbContext) : IRefundRepository
{
    public Task<REFUND?> GetByIdAsync(Guid refundId, CancellationToken cancellationToken) =>
        dbContext.Refunds().FirstOrDefaultAsync(r => r.REFUND_ID == refundId, cancellationToken);

    public async Task AddAsync(REFUND refund, CancellationToken cancellationToken)
    {
        dbContext.Refunds().Add(refund);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<REFUND>> GetPendingAsync(CancellationToken cancellationToken)
    {
        return await dbContext.Refunds()
            .Where(r => r.STATUS == RefundStatus.Requested)
            .OrderByDescending(r => r.REQUESTED_AT_UTC)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<(IReadOnlyList<REFUND> Items, int TotalCount)> ListPendingPagedAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.Refunds().Where(r => r.STATUS == RefundStatus.Requested);
        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(r => r.REQUESTED_AT_UTC)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (items, totalCount);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
