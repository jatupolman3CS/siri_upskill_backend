using Hangfire;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Siri.Modules.Commerce.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Infrastructure;

/// <summary>
/// Daily Hangfire recurring job that reconciles local Orders with Payment statuses.
/// </summary>
public sealed class StripeReconciliationJob(
    AppDbContext dbContext,
    IClock clock,
    ILogger<StripeReconciliationJob> logger)
{
    [DisableConcurrentExecution(timeoutInSeconds: 120)]
    public async Task RunAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Starting Stripe Reconciliation Job at {NowUtc}", clock.UtcNow);

        var expiredCutoff = clock.UtcNow.AddHours(-24);

        var expiredOrders = await dbContext.Set<ORDER>()
            .Where(o => o.STATUS == OrderStatus.AwaitingPayment && o.CreatedAtUtc <= expiredCutoff)
            .Take(100)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        foreach (var order in expiredOrders)
        {
            order.MarkCancelled();
            logger.LogInformation("Order {OrderId} marked cancelled due to payment expiry", order.ORDER_ID);
        }

        if (expiredOrders.Count > 0)
        {
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        logger.LogInformation("Completed Stripe Reconciliation Job, processed {Count} expired orders", expiredOrders.Count);
    }
}
