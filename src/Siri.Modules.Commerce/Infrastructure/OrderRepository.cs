using Microsoft.EntityFrameworkCore;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Persistence;

using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Infrastructure;

public sealed class OrderRepository(AppDbContext dbContext) : IOrderRepository
{
    public Task<ORDER?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken) =>
        dbContext.Orders().Include(o => o.ORDER_ITEMS).FirstOrDefaultAsync(o => o.ORDER_ID == orderId, cancellationToken);

    public async Task AddAsync(ORDER order, CancellationToken cancellationToken)
    {
        dbContext.Orders().Add(order);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<ORDER>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        await dbContext.Orders()
            .Include(o => o.ORDER_ITEMS)
            .Where(o => o.USER_ID == userId && (o.STATUS == OrderStatus.Pending || o.STATUS == OrderStatus.AwaitingPayment || o.STATUS == OrderStatus.Paid))
            .OrderByDescending(o => o.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);

    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(operation);

        if (dbContext.Database.CurrentTransaction is not null)
        {
            return await operation().ConfigureAwait(false);
        }

        var strategy = dbContext.Database.CreateExecutionStrategy();
        return await strategy.ExecuteAsync(async () =>
        {
            await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
            try
            {
                var result = await operation().ConfigureAwait(false);
                if (result is Result { IsSuccess: false })
                {
                    await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                }
                else
                {
                    await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
                }
                return result;
            }
            catch
            {
                await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
                throw;
            }
        }).ConfigureAwait(false);
    }
}
