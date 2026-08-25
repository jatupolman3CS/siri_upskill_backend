using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.CreateLearningPath;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;

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

public sealed class GetLearningPathsHandler(AppDbContext dbContext)
{
    public async Task<IReadOnlyList<LearningPathSummaryResponse>> HandleAsync(bool activeOnly, CancellationToken cancellationToken)
    {
        var query = dbContext.LearningPaths().AsNoTracking();

        if (activeOnly)
        {
            query = query.Where(p => p.IsActive);
        }

        var paths = await query
            .OrderBy(p => p.SortOrder)
            .ThenByDescending(p => p.CreatedAtUtc)
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

        return paths;
    }
}
