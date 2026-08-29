using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Domain;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Infrastructure;

public sealed class CourseReviewRepository(AppDbContext dbContext) : ICourseReviewRepository
{
    public Task<COURSE_REVIEW?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.CourseReviews().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);

    public Task<COURSE_REVIEW?> GetUserReviewForCourseAsync(Guid userId, Guid courseId, CancellationToken cancellationToken) =>
        dbContext.CourseReviews().FirstOrDefaultAsync(r => r.UserId == userId && r.CourseId == courseId, cancellationToken);

    public async Task<(IReadOnlyList<COURSE_REVIEW> Items, int TotalCount)> GetByCourseIdPagedAsync(Guid courseId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var query = dbContext.CourseReviews()
            .Where(r => r.CourseId == courseId && r.IsPublished);

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);
        var items = await query
            .OrderByDescending(r => r.CreatedAtUtc)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return (items, totalCount);
    }

    public async Task<IReadOnlyList<COURSE_REVIEW>> GetPendingReviewsAsync(CancellationToken cancellationToken) =>
        await dbContext.CourseReviews()
            .Where(r => !r.IsPublished)
            .OrderBy(r => r.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task AddAsync(COURSE_REVIEW review, CancellationToken cancellationToken)
    {
        dbContext.CourseReviews().Add(review);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(COURSE_REVIEW review, CancellationToken cancellationToken)
    {
        dbContext.CourseReviews().Update(review);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
