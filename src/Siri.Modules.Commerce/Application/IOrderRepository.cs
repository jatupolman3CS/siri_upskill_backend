using Siri.Modules.Commerce.Domain;

namespace Siri.Modules.Commerce.Application;

public interface IOrderRepository
{
    Task<ORDER?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken);

    Task AddAsync(ORDER order, CancellationToken cancellationToken);

    /// <summary>Complex aggregate query (every order of a user that is not in a terminal status) — left
    /// as a stub for whoever builds the "my orders" listing use case; the exact definition of "active"
    /// (which <see cref="OrderStatus"/> values count) is a product decision, not something this scaffold
    /// pass should guess.</summary>
    Task<IReadOnlyList<ORDER>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken);

    Task<(IReadOnlyList<ORDER> Items, int TotalCount)> ListByUserIdAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken);

    Task<IReadOnlyList<ORDER>> GetStaleAwaitingPaymentOrdersAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Executes the specified asynchronous operation within a database transaction boundary.
    /// Automatically commits if the operation succeeds (or returns a successful Result), and rolls back on failure or exception.
    /// </summary>
    Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken);

    Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken);
}
