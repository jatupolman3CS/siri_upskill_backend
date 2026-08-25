using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.GetCourseBuilder;

public sealed class GetCourseBuilderHandler(AppDbContext dbContext)
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบคอร์สนี้");
    private static readonly DomainError NotOwnerError = DomainError.Forbidden("คุณไม่มีสิทธิ์เข้าถึงคอร์สนี้");

    public async Task<Result<CourseBuilderResponse>> HandleAsync(Guid userId, Guid courseId, CancellationToken cancellationToken)
    {
        var course = await dbContext.Courses()
            .AsNoTracking()
            .Include(c => c.Sections).ThenInclude(s => s.Episodes)
            .Include(c => c.Outcomes)
            .Include(c => c.Requirements)
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken)
            .ConfigureAwait(false);

        if (course is null)
        {
            return Result.Failure<CourseBuilderResponse>(NotFoundError);
        }

        var instructorProfile = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (instructorProfile is null || course.InstructorId != instructorProfile.Id)
        {
            return Result.Failure<CourseBuilderResponse>(NotOwnerError);
        }

        var sections = course.Sections
            .OrderBy(s => s.SortOrder)
            .Select(s => new CourseBuilderSectionResponse(
                s.Id,
                s.Title,
                s.SortOrder,
                s.Episodes
                    .OrderBy(e => e.SortOrder)
                    .Select(e => new CourseBuilderEpisodeResponse(
                        e.Id,
                        e.SectionId,
                        e.Title,
                        e.Description,
                        e.SortOrder,
                        e.IsFreePreview,
                        e.MediaAssetId,
                        e.DurationSeconds,
                        e.Status))
                    .ToList()))
            .ToList();

        var outcomes = course.Outcomes.OrderBy(o => o.SortOrder).Select(o => o.Text).ToList();
        var requirements = course.Requirements.OrderBy(r => r.SortOrder).Select(r => r.Text).ToList();

        return new CourseBuilderResponse(
            course.Id,
            course.Slug,
            course.Title,
            course.Subtitle,
            course.Description,
            course.InstructorId,
            course.CategoryId,
            course.Level,
            course.Language,
            course.ThumbnailUrl,
            course.Price,
            course.ComparePrice,
            course.Currency,
            course.AccessDurationDays,
            course.Status,
            course.SeoTitle,
            course.SeoDescription,
            course.RowVersion,
            outcomes,
            requirements,
            sections);
    }
}
