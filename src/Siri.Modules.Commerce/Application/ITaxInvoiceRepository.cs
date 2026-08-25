using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Application;

public interface ITaxInvoiceRepository
{
    Task<TAX_INVOICE?> GetByIdAsync(Guid taxInvoiceId, CancellationToken cancellationToken);

    Task AddAsync(TAX_INVOICE taxInvoice, CancellationToken cancellationToken);

    /// <summary>Admin-facing paginated list — left as a stub; same deferred-filter reasoning as
    /// <see cref="IBundleRepository.ListAsync"/>'s own doc comment (here: which date range / buyer / status
    /// the finance back office needs is not specified by this scaffold task).</summary>
    Task<IReadOnlyList<TAX_INVOICE>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<int> CountAsync(CancellationToken cancellationToken);
}
