using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Application;

public interface IPaymentRepository
{
    Task<PAYMENT?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken);

    /// <summary>The lookup a Stripe webhook handler needs: "which of our payments is this event about" —
    /// <see cref="PAYMENT.PROVIDER_PAYMENT_INTENT_ID"/> is unique, so this is as trivial/safe a fetch as
    /// <see cref="GetByIdAsync"/>.</summary>
    Task<PAYMENT?> GetByProviderPaymentIntentIdAsync(string providerPaymentIntentId, CancellationToken cancellationToken);

    Task AddAsync(PAYMENT payment, CancellationToken cancellationToken);
}
