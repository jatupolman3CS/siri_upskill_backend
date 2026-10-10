using Microsoft.EntityFrameworkCore;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Persistence;

namespace Siri.Modules.Commerce.Infrastructure;

public sealed class PaymentAmountOverrideRepository(AppDbContext dbContext) : IPaymentAmountOverrideRepository
{
    public Task<PAYMENT_AMOUNT_OVERRIDE?> GetCurrentAsync(CancellationToken cancellationToken) =>
        dbContext.PaymentAmountOverrides()
            .AsNoTracking()
            .OrderByDescending(o => o.CHANGED_AT_UTC)
            .ThenByDescending(o => o.PAYMENT_AMOUNT_OVERRIDE_ID)
            .FirstOrDefaultAsync(cancellationToken);

    public Task<int> CountAsync(CancellationToken cancellationToken) =>
        dbContext.PaymentAmountOverrides().CountAsync(cancellationToken);

    public async Task<IReadOnlyList<PAYMENT_AMOUNT_OVERRIDE>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        await dbContext.PaymentAmountOverrides()
            .AsNoTracking()
            .OrderByDescending(o => o.CHANGED_AT_UTC)
            .ThenByDescending(o => o.PAYMENT_AMOUNT_OVERRIDE_ID)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task AddAsync(PAYMENT_AMOUNT_OVERRIDE entry, CancellationToken cancellationToken)
    {
        dbContext.PaymentAmountOverrides().Add(entry);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
