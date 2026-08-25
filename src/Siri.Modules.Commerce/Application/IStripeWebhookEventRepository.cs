using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Application;

/// <summary>No accompanying Service/Endpoints — see <see cref="STRIPE_WEBHOOK_EVENT"/>'s own doc comment.
/// A later task's webhook handler calls this repository directly.</summary>
public interface IStripeWebhookEventRepository
{
    /// <summary>The idempotency check a webhook handler runs first, before doing anything else with a
    /// delivery — security.md: "Webhook ต้อง idempotent".</summary>
    Task<STRIPE_WEBHOOK_EVENT?> GetByStripeEventIdAsync(string stripeEventId, CancellationToken cancellationToken);

    Task AddAsync(STRIPE_WEBHOOK_EVENT webhookEvent, CancellationToken cancellationToken);
}
