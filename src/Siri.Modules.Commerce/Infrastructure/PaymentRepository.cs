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
}
