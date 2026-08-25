using Microsoft.EntityFrameworkCore;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Persistence;

namespace Siri.Modules.Identity.Features.Admin.GetAdminUsers;

public sealed record AdminUserSummaryResponse(
    Guid Id,
    string Email,
    string DisplayName,
    UserStatus Status,
    IReadOnlyList<string> Roles,
    DateTime CreatedAtUtc,
    DateTime? LastLoginAtUtc);

public sealed record GetAdminUsersResult(
    IReadOnlyList<AdminUserSummaryResponse> Items,
    int TotalCount,
    int Page,
    int PageSize);

public sealed class GetAdminUsersHandler(AppDbContext dbContext)
{
    public async Task<GetAdminUsersResult> HandleAsync(
        string? query,
        string? role,
        UserStatus? status,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var usersQuery = dbContext.Users()
            .AsNoTracking()
            .Include(u => u.Roles)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(query))
        {
            var search = query.Trim().ToUpperInvariant();
            usersQuery = usersQuery.Where(u => u.NormalizedEmail.Contains(search) || u.DisplayName.Contains(query.Trim()));
        }

        if (status.HasValue)
        {
            usersQuery = usersQuery.Where(u => u.Status == status.Value);
        }

        if (!string.IsNullOrWhiteSpace(role))
        {
            usersQuery = usersQuery.Where(u => u.Roles.Any(r => r.Name == role.Trim()));
        }

        var totalCount = await usersQuery.CountAsync(cancellationToken).ConfigureAwait(false);

        var users = await usersQuery
            .OrderByDescending(u => u.CreatedAtUtc)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(u => new AdminUserSummaryResponse(
                u.Id,
                u.Email,
                u.DisplayName,
                u.Status,
                u.Roles.Select(r => r.Name).ToList(),
                u.CreatedAtUtc,
                u.LastLoginAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new GetAdminUsersResult(users, totalCount, page, pageSize);
    }
}
