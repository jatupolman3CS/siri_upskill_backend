using Microsoft.EntityFrameworkCore;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Persistence;

namespace Siri.Modules.Commerce.Infrastructure;

public sealed class StripeWebhookEventRepository(AppDbContext dbContext) : IStripeWebhookEventRepository
{
    public Task<STRIPE_WEBHOOK_EVENT?> GetByStripeEventIdAsync(string stripeEventId, CancellationToken cancellationToken) =>
        dbContext.StripeWebhookEvents().FirstOrDefaultAsync(e => e.STRIPE_EVENT_ID == stripeEventId, cancellationToken);

    public async Task AddAsync(STRIPE_WEBHOOK_EVENT webhookEvent, CancellationToken cancellationToken)
    {
        dbContext.StripeWebhookEvents().Add(webhookEvent);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
