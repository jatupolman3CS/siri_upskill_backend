using Microsoft.EntityFrameworkCore;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Persistence;

namespace Siri.Modules.Identity.Features.Admin.GetAdminAuditLogs;

public sealed record AdminAuditLogResponse(
    Guid Id,
    Guid? UserId,
    string EventType,
    string? Detail,
    string? IpAddress,
    DateTime OccurredAtUtc);

public sealed record GetAdminAuditLogsResult(
    IReadOnlyList<AdminAuditLogResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed class GetAdminAuditLogsHandler(AppDbContext dbContext)
{
    public async Task<GetAdminAuditLogsResult> HandleAsync(
        Guid? userId,
        string? eventType,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = dbContext.SecurityAudits()
            .AsNoTracking()
            .AsQueryable();

        if (userId.HasValue)
        {
            query = query.Where(a => a.UserId == userId.Value);
        }

        if (!string.IsNullOrWhiteSpace(eventType))
        {
            query = query.Where(a => a.EventType == eventType.Trim());
        }

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .OrderByDescending(a => a.OccurredAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(a => new AdminAuditLogResponse(
                a.Id,
                a.UserId,
                a.EventType,
                a.Detail,
                a.IpAddress,
                a.OccurredAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new GetAdminAuditLogsResult(items, totalCount, page, pageSize);
    }
}
