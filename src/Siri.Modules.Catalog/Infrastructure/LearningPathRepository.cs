using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Domain;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Infrastructure;

public sealed class LearningPathRepository(AppDbContext dbContext) : ILearningPathRepository
{
    public async Task<IReadOnlyList<LEARNING_PATH>> GetAllAsync(bool activeOnly, CancellationToken cancellationToken)
    {
        var query = dbContext.LearningPaths().AsQueryable();
        if (activeOnly)
        {
            query = query.Where(lp => lp.IsActive);
        }

        return await query
            .OrderBy(lp => lp.SortOrder)
            .ThenByDescending(lp => lp.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public Task<LEARNING_PATH?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.LearningPaths().FirstOrDefaultAsync(lp => lp.Id == id, cancellationToken);

    public Task<LEARNING_PATH?> GetBySlugAsync(string slug, CancellationToken cancellationToken) =>
        dbContext.LearningPaths().FirstOrDefaultAsync(lp => lp.Slug == slug, cancellationToken);

    public Task<LEARNING_PATH?> GetWithItemsByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.LearningPaths()
            .Include(lp => lp.Items.OrderBy(i => i.SortOrder))
            .FirstOrDefaultAsync(lp => lp.Id == id, cancellationToken);

    public Task<LEARNING_PATH?> GetWithItemsBySlugAsync(string slug, CancellationToken cancellationToken) =>
        dbContext.LearningPaths()
            .Include(lp => lp.Items.OrderBy(i => i.SortOrder))
            .FirstOrDefaultAsync(lp => lp.Slug == slug, cancellationToken);

    public async Task AddAsync(LEARNING_PATH learningPath, CancellationToken cancellationToken)
    {
        dbContext.LearningPaths().Add(learningPath);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(LEARNING_PATH learningPath, CancellationToken cancellationToken)
    {
        dbContext.LearningPaths().Update(learningPath);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task DeleteAsync(LEARNING_PATH learningPath, CancellationToken cancellationToken)
    {
        dbContext.LearningPaths().Remove(learningPath);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
