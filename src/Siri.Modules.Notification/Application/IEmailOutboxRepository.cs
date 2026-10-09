using Siri.Modules.Notification.Domain;

namespace Siri.Modules.Notification.Application;

public interface IEmailOutboxRepository
{
    Task<IReadOnlyList<EMAIL_OUTBOX_MESSAGE>> GetPendingMessagesAsync(int batchSize, CancellationToken cancellationToken);

    /// <summary>The outbox row with this id, tracked for update, or <c>null</c> when it does not exist.</summary>
    Task<EMAIL_OUTBOX_MESSAGE?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task AddAsync(EMAIL_OUTBOX_MESSAGE message, CancellationToken cancellationToken);

    Task UpdateAsync(EMAIL_OUTBOX_MESSAGE message, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
