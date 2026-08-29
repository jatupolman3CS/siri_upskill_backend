using Siri.Modules.Identity.Domain;

namespace Siri.Modules.Identity.Application;

public interface IUserRepository
{
    Task<USER?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<USER?> GetByEmailAsync(string email, CancellationToken cancellationToken);

    Task<USER?> GetWithRolesByIdAsync(Guid id, CancellationToken cancellationToken);

    Task<USER?> GetWithRolesByEmailAsync(string email, CancellationToken cancellationToken);

    Task<(IReadOnlyList<USER> Items, int TotalCount)> GetPagedUsersAsync(int page, int pageSize, string? search, CancellationToken cancellationToken);

    Task AddAsync(USER user, CancellationToken cancellationToken);

    Task UpdateAsync(USER user, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken);

    Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken);
}
