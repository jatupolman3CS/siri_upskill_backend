using Microsoft.EntityFrameworkCore;
using Siri.Modules.Notification.Domain;
using Siri.Modules.Notification.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Notification.Features.GetContactMessages;

public sealed class GetContactMessagesHandler(AppDbContext dbContext)
{
    public async Task<PagedResult<ContactMessageListItemResponse>> HandleAsync(
        GetContactMessagesQuery query,
        CancellationToken cancellationToken)
    {
        var page = query.Page < 1 ? 1 : query.Page;
        var pageSize = query.PageSize is < 1 or > 100 ? 20 : query.PageSize;

        IQueryable<CONTACT_MESSAGE> messagesQuery = dbContext.ContactMessages().AsNoTracking();

        if (query.Status.HasValue)
        {
            messagesQuery = messagesQuery.Where(m => m.Status == query.Status.Value);
        }

        var totalCount = await messagesQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await messagesQuery
            .OrderByDescending(m => m.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(m => new ContactMessageListItemResponse(
                m.Id,
                m.Name,
                m.Email,
                m.Subject,
                m.Message,
                m.Status,
                m.ResolvedAtUtc,
                m.ResolvedBy,
                m.AdminNotes,
                m.CreatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return PagedResult<ContactMessageListItemResponse>.Create(items, totalCount, page, pageSize);
    }
}
