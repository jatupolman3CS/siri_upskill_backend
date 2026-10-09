using Microsoft.EntityFrameworkCore;
using Siri.Modules.Notification.Application;
using Siri.Modules.Notification.Domain;
using Siri.Persistence;

namespace Siri.Modules.Notification.Infrastructure;

public sealed class EmailOutboxRepository(AppDbContext dbContext) : IEmailOutboxRepository
{
    public async Task<IReadOnlyList<EMAIL_OUTBOX_MESSAGE>> GetPendingMessagesAsync(int batchSize, CancellationToken cancellationToken) =>
        await dbContext.EmailOutboxMessages()
            .Where(m => m.Status == EmailOutboxStatus.Pending || (m.Status == EmailOutboxStatus.Failed && m.NextRetryAtUtc <= DateTime.UtcNow))
            .OrderBy(m => m.Id)
            .Take(batchSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<EMAIL_OUTBOX_MESSAGE?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.EmailOutboxMessages().FirstOrDefaultAsync(m => m.Id == id, cancellationToken);

    public async Task AddAsync(EMAIL_OUTBOX_MESSAGE message, CancellationToken cancellationToken)
    {
        dbContext.EmailOutboxMessages().Add(message);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(EMAIL_OUTBOX_MESSAGE message, CancellationToken cancellationToken)
    {
        dbContext.EmailOutboxMessages().Update(message);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
