using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Application;

/// <summary>Append-only store for <see cref="PAYMENT_AMOUNT_OVERRIDE"/> — there is intentionally no update or
/// delete.</summary>
public interface IPaymentAmountOverrideRepository
{
    /// <summary>The newest entry (the setting currently in force), or <c>null</c> when an override was never
    /// configured.</summary>
    Task<PAYMENT_AMOUNT_OVERRIDE?> GetCurrentAsync(CancellationToken cancellationToken);

    Task<int> CountAsync(CancellationToken cancellationToken);

    /// <summary>Newest first.</summary>
    Task<IReadOnlyList<PAYMENT_AMOUNT_OVERRIDE>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task AddAsync(PAYMENT_AMOUNT_OVERRIDE entry, CancellationToken cancellationToken);
}
