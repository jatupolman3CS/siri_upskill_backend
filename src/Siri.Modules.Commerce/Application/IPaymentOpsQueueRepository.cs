using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Application;

/// <summary>No accompanying Service/Endpoints — see <see cref="PAYMENT_OPS_QUEUE"/>'s own doc comment.
/// The admin resolve-workflow UI is a later task.</summary>
public interface IPaymentOpsQueueRepository
{
    Task<PAYMENT_OPS_QUEUE?> GetByIdAsync(Guid paymentOpsQueueId, CancellationToken cancellationToken);

    Task AddAsync(PAYMENT_OPS_QUEUE entry, CancellationToken cancellationToken);

    /// <summary>Admin queue listing (open/in-progress entries, most likely paginated via
    /// <see cref="Siri.SharedKernel.PagedResult{T}"/>) — left as a stub; the exact filter/sort the admin
    /// ops screen needs is not specified by this scaffold task.</summary>
    Task<IReadOnlyList<PAYMENT_OPS_QUEUE>> GetOpenEntriesAsync(CancellationToken cancellationToken);
}
