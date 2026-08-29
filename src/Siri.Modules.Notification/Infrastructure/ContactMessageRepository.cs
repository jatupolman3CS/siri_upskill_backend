using Microsoft.EntityFrameworkCore;
using Siri.Modules.Notification.Application;
using Siri.Modules.Notification.Domain;
using Siri.Persistence;

namespace Siri.Modules.Notification.Infrastructure;

public sealed class ContactMessageRepository(AppDbContext dbContext) : IContactMessageRepository
{
    public Task<CONTACT_MESSAGE?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.ContactMessages().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task<(IReadOnlyList<CONTACT_MESSAGE> Items, int TotalCount)> GetPagedAsync(ContactMessageStatus? status, int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var query = dbContext.ContactMessages().AsQueryable();
        if (status.HasValue)
        {
            query = query.Where(m => m.Status == status.Value);
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(m => m.CreatedAtUtc)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (items, totalCount);
    }

    public async Task AddAsync(CONTACT_MESSAGE message, CancellationToken cancellationToken)
    {
        dbContext.ContactMessages().Add(message);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(CONTACT_MESSAGE message, CancellationToken cancellationToken)
    {
        dbContext.ContactMessages().Update(message);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
