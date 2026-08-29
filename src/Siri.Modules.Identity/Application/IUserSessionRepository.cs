using Siri.Modules.Identity.Domain;

namespace Siri.Modules.Identity.Application;

public interface IUserSessionRepository
{
    Task<USER_SESSION?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<IReadOnlyList<USER_SESSION>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task AddAsync(USER_SESSION session, CancellationToken cancellationToken);

    Task UpdateAsync(USER_SESSION session, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
