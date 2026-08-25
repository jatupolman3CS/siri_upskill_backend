using Microsoft.EntityFrameworkCore;
using Siri.Modules.Payout.Application;
using Siri.Modules.Payout.Domain;
using Siri.Persistence;

namespace Siri.Modules.Payout.Infrastructure;

public sealed class RevenueSplitRepository(AppDbContext dbContext) : IRevenueSplitRepository
{
    public Task<REVENUE_SPLIT?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.RevenueSplits().FirstOrDefaultAsync(r => r.REVENUE_SPLIT_ID == id, cancellationToken);

    public Task<REVENUE_SPLIT?> GetByOrderItemIdAsync(Guid orderItemId, CancellationToken cancellationToken) =>
        dbContext.RevenueSplits().FirstOrDefaultAsync(r => r.ORDER_ITEM_ID == orderItemId, cancellationToken);

    public IQueryable<REVENUE_SPLIT> Query() => dbContext.RevenueSplits().AsNoTracking();

    public async Task<decimal> GetTotalEarningsAsync(Guid instructorId, CancellationToken cancellationToken) =>
        await dbContext.RevenueSplits()
            .AsNoTracking()
            .Where(r => r.INSTRUCTOR_ID == instructorId)
            .SumAsync(r => (decimal?)r.INSTRUCTOR_AMOUNT, cancellationToken)
            .ConfigureAwait(false) ?? 0m;

    public async Task<decimal> GetPendingEarningsAsync(Guid instructorId, CancellationToken cancellationToken) =>
        await dbContext.RevenueSplits()
            .AsNoTracking()
            .Where(r => r.INSTRUCTOR_ID == instructorId && r.STATUS == RevenueSplitStatus.Pending)
            .SumAsync(r => (decimal?)r.INSTRUCTOR_AMOUNT, cancellationToken)
            .ConfigureAwait(false) ?? 0m;

    public void Add(REVENUE_SPLIT revenueSplit) => dbContext.RevenueSplits().Add(revenueSplit);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
