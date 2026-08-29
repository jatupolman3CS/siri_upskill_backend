using Microsoft.EntityFrameworkCore;
using Siri.Modules.Identity.Application;
using Siri.Modules.Identity.Domain;
using Siri.Persistence;

namespace Siri.Modules.Identity.Infrastructure;

public sealed class UserRepository(AppDbContext dbContext) : IUserRepository
{
    public Task<USER?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Users().FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<USER?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
        dbContext.Users().FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

    public Task<USER?> GetWithRolesByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Users()
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == id, cancellationToken);

    public Task<USER?> GetWithRolesByEmailAsync(string email, CancellationToken cancellationToken) =>
        dbContext.Users()
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Email == email, cancellationToken);

    public async Task<(IReadOnlyList<USER> Items, int TotalCount)> GetPagedUsersAsync(int page, int pageSize, string? search, CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var query = dbContext.Users()
            .Include(u => u.Roles)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var trimmed = search.Trim();
            query = query.Where(u => u.Email.Contains(trimmed) || u.DisplayName.Contains(trimmed));
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(u => u.CreatedAtUtc)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (items, totalCount);
    }

    public async Task AddAsync(USER user, CancellationToken cancellationToken)
    {
        dbContext.Users().Add(user);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(USER user, CancellationToken cancellationToken)
    {
        dbContext.Users().Update(user);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);

    public async Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            return await operation().ConfigureAwait(false);
        }

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var result = await operation().ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
            return result;
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }

    public async Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken)
    {
        if (dbContext.Database.CurrentTransaction is not null)
        {
            await operation().ConfigureAwait(false);
            return;
        }

        await using var tx = await dbContext.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await operation().ConfigureAwait(false);
            await tx.CommitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            await tx.RollbackAsync(cancellationToken).ConfigureAwait(false);
            throw;
        }
    }
}
