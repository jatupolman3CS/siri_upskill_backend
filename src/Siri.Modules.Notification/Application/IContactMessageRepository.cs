using Siri.Modules.Notification.Domain;

namespace Siri.Modules.Notification.Application;

public interface IContactMessageRepository
{
    Task<CONTACT_MESSAGE?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<(IReadOnlyList<CONTACT_MESSAGE> Items, int TotalCount)> GetPagedAsync(ContactMessageStatus? status, int page, int pageSize, CancellationToken cancellationToken);

    Task AddAsync(CONTACT_MESSAGE message, CancellationToken cancellationToken);

    Task UpdateAsync(CONTACT_MESSAGE message, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
