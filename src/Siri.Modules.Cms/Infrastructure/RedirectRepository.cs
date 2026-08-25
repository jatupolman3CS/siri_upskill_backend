using Microsoft.EntityFrameworkCore;
using Siri.Modules.Cms.Application;
using Siri.Modules.Cms.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Cms.Infrastructure;

public sealed class RedirectRepository(AppDbContext dbContext) : IRedirectRepository
{
    public Task<REDIRECT?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Redirects().FirstOrDefaultAsync(r => r.REDIRECT_ID == id, cancellationToken);

    public Task<REDIRECT?> GetByFromPathAsync(string fromPath, CancellationToken cancellationToken) =>
        dbContext.Redirects().FirstOrDefaultAsync(r => r.FROM_PATH == fromPath, cancellationToken);

    public Task<bool> FromPathExistsAsync(string fromPath, Guid? excludeRedirectId, CancellationToken cancellationToken) =>
        dbContext.Redirects().AnyAsync(r => r.FROM_PATH == fromPath && (!excludeRedirectId.HasValue || r.REDIRECT_ID != excludeRedirectId.Value), cancellationToken);

    public async Task<PagedResult<REDIRECT>> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var totalCount = await dbContext.Redirects().CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await dbContext.Redirects()
            .OrderBy(r => r.FROM_PATH)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return PagedResult<REDIRECT>.Create(items, totalCount, page, pageSize);
    }

    public void Add(REDIRECT redirect) => dbContext.Redirects().Add(redirect);

    public void Remove(REDIRECT redirect) => dbContext.Redirects().Remove(redirect);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
