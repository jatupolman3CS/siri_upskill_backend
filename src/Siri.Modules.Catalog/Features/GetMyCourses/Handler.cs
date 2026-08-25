using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.GetMyCourses;

/// <summary>
/// The caller's own courses (any status, including drafts) — offset-paginated (database.md: "รายการที่
/// โตได้ต้อง paginate เสมอ" + "offset ได้สำหรับ admin table", same reasoning applies to an instructor's
/// own course list). Returns an empty page for a caller with no <c>InstructorProfile</c> at all (e.g. an
/// Admin/SuperAdmin who reaches this <c>InstructorOnly</c>-gated endpoint without owning any courses
/// themselves) rather than an error — "no courses" is a legitimate, unremarkable state, not a failure.
/// </summary>
public sealed class GetMyCoursesHandler(AppDbContext dbContext)
{
    public const int DefaultPageSize = 20;
    public const int MaxPageSize = 100;

    public async Task<PagedResult<CourseSummary>> HandleAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
    {
        var effectivePage = page < 1 ? 1 : page;
        var effectivePageSize = pageSize switch
        {
            < 1 => DefaultPageSize,
            > MaxPageSize => MaxPageSize,
            _ => pageSize,
        };

        var instructorProfile = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (instructorProfile is null)
        {
            return PagedResult<CourseSummary>.Create([], 0, effectivePage, effectivePageSize);
        }

        var query = dbContext.Courses()
            .AsNoTracking()
            .Where(c => c.InstructorId == instructorProfile.Id)
            .OrderByDescending(c => c.CreatedAtUtc);

        var totalCount = await query.CountAsync(cancellationToken).ConfigureAwait(false);

        var items = await query
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .Select(c => new CourseSummary(c.Id, c.Slug, c.Title, c.ThumbnailUrl, c.Price, c.Status, c.CreatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return PagedResult<CourseSummary>.Create(items, totalCount, effectivePage, effectivePageSize);
    }
}
