using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.CreateLearningPath;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.GetLearningPaths;

public sealed record LearningPathSummaryResponse(
    Guid Id,
    string Slug,
    string Title,
    string? Description,
    int SortOrder,
    bool IsActive,
    int CourseCount,
    DateTime CreatedAtUtc);

/// <summary>
/// Learning paths, offset-paginated — every other list endpoint in this module already uses
/// <see cref="PagedResult{T}"/> (see <c>GetPendingInstructorApplicationsHandler</c>, whose page/pageSize
/// clamping this mirrors exactly); this one previously returned the entire unbounded set with no
/// page/pageSize param at all.
/// </summary>
public sealed class GetLearningPathsHandler(AppDbContext dbContext)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public async Task<PagedResult<LearningPathSummaryResponse>> HandleAsync(
        bool activeOnly,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        // Clamped here, not the endpoint/controller — see GetPendingInstructorApplicationsHandler's own
        // comment for why this is a business rule, not an HTTP-binding concern.
        var effectivePage = page < 1 ? 1 : page;
        var effectivePageSize = pageSize switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => pageSize,
        };

        var query = dbContext.LearningPaths().AsNoTracking();

        if (activeOnly)
        {
            query = query.Where(p => p.IsActive);
        }

        query = query.OrderBy(p => p.SortOrder).ThenByDescending(p => p.CreatedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var paths = await query
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .Select(p => new LearningPathSummaryResponse(
                p.Id,
                p.Slug,
                p.Title,
                p.Description,
                p.SortOrder,
                p.IsActive,
                p.Items.Count,
                p.CreatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return PagedResult<LearningPathSummaryResponse>.Create(paths, totalCount, effectivePage, effectivePageSize);
    }
}
