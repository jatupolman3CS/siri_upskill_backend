using Microsoft.EntityFrameworkCore;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Persistence;

namespace Siri.Modules.Commerce.Infrastructure;

public sealed class TaxInvoiceRepository(AppDbContext dbContext) : ITaxInvoiceRepository
{
    public Task<TAX_INVOICE?> GetByIdAsync(Guid taxInvoiceId, CancellationToken cancellationToken) =>
        dbContext.TaxInvoices().FirstOrDefaultAsync(t => t.TAX_INVOICE_ID == taxInvoiceId, cancellationToken);

    public async Task AddAsync(TAX_INVOICE taxInvoice, CancellationToken cancellationToken)
    {
        dbContext.TaxInvoices().Add(taxInvoice);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<TAX_INVOICE>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        await dbContext.TaxInvoices()
            .OrderByDescending(t => t.ISSUED_AT_UTC)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<int> CountAsync(CancellationToken cancellationToken) =>
        dbContext.TaxInvoices().CountAsync(cancellationToken);
}
