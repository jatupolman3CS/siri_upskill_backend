using Siri.Modules.Notification.Domain;

namespace Siri.Modules.Notification.Application;

public interface IEmailOutboxRepository
{
    Task<IReadOnlyList<EMAIL_OUTBOX_MESSAGE>> GetPendingMessagesAsync(int batchSize, CancellationToken cancellationToken);

    Task AddAsync(EMAIL_OUTBOX_MESSAGE message, CancellationToken cancellationToken);

    Task UpdateAsync(EMAIL_OUTBOX_MESSAGE message, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
