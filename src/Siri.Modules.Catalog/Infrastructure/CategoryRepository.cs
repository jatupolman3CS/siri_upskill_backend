using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Domain;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Infrastructure;

public sealed class CategoryRepository(AppDbContext dbContext) : ICategoryRepository
{
    public async Task<IReadOnlyList<CATEGORY>> GetAllAsync(CancellationToken cancellationToken) =>
        await dbContext.Categories()
            .OrderBy(c => c.SortOrder)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<CATEGORY?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Categories().FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

    public Task<CATEGORY?> GetBySlugAsync(string slug, CancellationToken cancellationToken) =>
        dbContext.Categories().FirstOrDefaultAsync(c => c.Slug == slug, cancellationToken);

    public async Task AddAsync(CATEGORY category, CancellationToken cancellationToken)
    {
        dbContext.Categories().Add(category);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(CATEGORY category, CancellationToken cancellationToken)
    {
        dbContext.Categories().Update(category);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(CATEGORY category, CancellationToken cancellationToken)
    {
        dbContext.Categories().Remove(category);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
