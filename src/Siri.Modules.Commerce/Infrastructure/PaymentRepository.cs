using Microsoft.EntityFrameworkCore;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Persistence;

namespace Siri.Modules.Commerce.Infrastructure;

public sealed class PaymentRepository(AppDbContext dbContext) : IPaymentRepository
{
    public Task<PAYMENT?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken) =>
        dbContext.Payments().FirstOrDefaultAsync(p => p.PAYMENT_ID == paymentId, cancellationToken);

    public Task<PAYMENT?> GetByProviderPaymentIntentIdAsync(string providerPaymentIntentId, CancellationToken cancellationToken) =>
        dbContext.Payments().FirstOrDefaultAsync(p => p.PROVIDER_PAYMENT_INTENT_ID == providerPaymentIntentId, cancellationToken);

    public async Task AddAsync(PAYMENT payment, CancellationToken cancellationToken)
    {
        dbContext.Payments().Add(payment);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PAYMENT>> GetPendingByOrderIdAsync(Guid orderId, CancellationToken cancellationToken)
    {
        return await dbContext.Payments()
            .Where(p => p.ORDER_ID == orderId && (p.STATUS == PaymentStatus.Pending || p.STATUS == PaymentStatus.Processing))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyDictionary<Guid, Guid>> GetSucceededPaymentIdsByOrderIdsAsync(
        IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(orderIds);

        var ids = orderIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, Guid>();
        }

        var rows = await dbContext.Payments()
            .AsNoTracking()
            .Where(p => ids.Contains(p.ORDER_ID) && p.STATUS == PaymentStatus.Succeeded)
            .Select(p => new { p.ORDER_ID, p.PAYMENT_ID, p.CREATED_AT_UTC })
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return rows
            .GroupBy(r => r.ORDER_ID)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(r => r.CREATED_AT_UTC).First().PAYMENT_ID);
    }
}
