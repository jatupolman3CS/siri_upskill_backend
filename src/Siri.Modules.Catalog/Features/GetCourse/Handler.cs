using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.GetCourse;

/// <summary>Reads one of the caller's own courses by id. Same ownership check as
/// <c>UpdateCourseHandler</c>/<c>DeleteCourseHandler</c> (distinct 404/403, no admin-bypass — see
/// <c>UpdateCourseHandler</c>'s own doc comment for the full reasoning) — not Draft-restricted, unlike
/// Update/Delete: reading your own course regardless of status is harmless, only mutating a non-draft one
/// is what P1-05 needs to own.</summary>
public sealed class GetCourseHandler(AppDbContext dbContext)
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบคอร์สนี้");
    private static readonly DomainError NotOwnerError = DomainError.Forbidden("คุณไม่มีสิทธิ์ดูคอร์สนี้");

    public async Task<Result<CourseResponse>> HandleAsync(Guid userId, Guid courseId, CancellationToken cancellationToken)
    {
        var course = await dbContext.Courses()
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken)
            .ConfigureAwait(false);

        if (course is null)
        {
            return Result.Failure<CourseResponse>(NotFoundError);
        }

        var instructorProfile = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (instructorProfile is null || course.InstructorId != instructorProfile.Id)
        {
            return Result.Failure<CourseResponse>(NotOwnerError);
        }

        return new CourseResponse(
            course.Id, course.Slug, course.Title, course.Subtitle, course.Description, course.InstructorId,
            course.CategoryId, course.Level, course.Language, course.ThumbnailUrl, course.Price, course.ComparePrice,
            course.Currency, course.AccessDurationDays, course.Status, course.SeoTitle, course.SeoDescription);
    }
}
