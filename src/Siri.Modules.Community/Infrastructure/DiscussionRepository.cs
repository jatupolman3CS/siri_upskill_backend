using Microsoft.EntityFrameworkCore;
using Siri.Modules.Community.Application;
using Siri.Modules.Community.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Community.Infrastructure;

public sealed class DiscussionRepository(AppDbContext context) : IDiscussionRepository
{
    public Task<DISCUSSION?> GetByIdAsync(Guid discussionId, CancellationToken cancellationToken) =>
        context.Discussions().FirstOrDefaultAsync(d => d.DISCUSSION_ID == discussionId, cancellationToken);

    public async Task<PagedResult<DISCUSSION>> ListByEpisodeAsync(Guid episodeId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var query = context.Discussions()
            .Where(d => d.EPISODE_ID == episodeId && d.STATUS == DiscussionStatus.Visible);

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(d => d.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return PagedResult<DISCUSSION>.Create(items, totalCount, page, pageSize);
    }

    public void Add(DISCUSSION discussion) => context.Discussions().Add(discussion);

    public void Remove(DISCUSSION discussion) => context.Discussions().Remove(discussion);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
