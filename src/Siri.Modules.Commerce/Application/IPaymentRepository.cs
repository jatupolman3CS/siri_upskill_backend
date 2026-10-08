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

    Task<IReadOnlyList<PAYMENT>> GetPendingByOrderIdAsync(Guid orderId, CancellationToken cancellationToken);

    /// <summary>
    /// For each of <paramref name="orderIds"/> that has a <see cref="PaymentStatus.Succeeded"/> payment, the id of that payment (untracked, one query — the order list
    /// uses it to tell the client which payment a refund request refers to). Orders without a successful payment are absent. Should an order ever carry more than one
    /// succeeded payment the most recent wins. Default implementation reports none, so existing test doubles of this interface keep compiling — only
    /// <c>Infrastructure.PaymentRepository</c> overrides it.
    /// </summary>
    Task<IReadOnlyDictionary<Guid, Guid>> GetSucceededPaymentIdsByOrderIdsAsync(
        IReadOnlyCollection<Guid> orderIds,
        CancellationToken cancellationToken) =>
        Task.FromResult<IReadOnlyDictionary<Guid, Guid>>(new Dictionary<Guid, Guid>());
}
