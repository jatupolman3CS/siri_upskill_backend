using Microsoft.EntityFrameworkCore;
using Siri.Modules.Cms.Application;
using Siri.Modules.Cms.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Cms.Infrastructure;

public sealed class PostRepository(AppDbContext dbContext) : IPostRepository
{
    public Task<POST?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Posts().FirstOrDefaultAsync(p => p.POST_ID == id, cancellationToken);

    public Task<POST?> GetPublishedBySlugAsync(string slug, CancellationToken cancellationToken) =>
        dbContext.Posts().FirstOrDefaultAsync(p => p.SLUG == slug && p.STATUS == PostStatus.Published, cancellationToken);

    public Task<bool> SlugExistsAsync(string slug, Guid? excludePostId, CancellationToken cancellationToken) =>
        dbContext.Posts().AnyAsync(p => p.SLUG == slug && (!excludePostId.HasValue || p.POST_ID != excludePostId.Value), cancellationToken);

    public async Task<PagedResult<POST>> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var totalCount = await dbContext.Posts().CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await dbContext.Posts()
            .OrderByDescending(p => p.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return PagedResult<POST>.Create(items, totalCount, page, pageSize);
    }

    public async Task<PagedResult<POST>> GetPublishedPagedAsync(int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = dbContext.Posts().Where(p => p.STATUS == PostStatus.Published);
        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(p => p.PUBLISHED_AT_UTC ?? p.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return PagedResult<POST>.Create(items, totalCount, page, pageSize);
    }

    public void Add(POST post) => dbContext.Posts().Add(post);

    public void Remove(POST post) => dbContext.Posts().Remove(post);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
