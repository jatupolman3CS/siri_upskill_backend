using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.DeleteCourse;

/// <summary>
/// Deletes a draft course — soft, not hard: <see cref="Course"/> implements
/// <see cref="Siri.Persistence.Conventions.ISoftDelete"/>, so <c>AuditableEntityInterceptor</c> turns this
/// <c>Remove()</c> call into an <c>IsDeleted</c> flag update automatically (see that interceptor's own
/// doc comment) — no explicit domain "delete" method needed on <see cref="Course"/> itself.
/// <para>
/// Same ownership check and Draft-only gate as <c>UpdateCourseHandler</c> — see that class's own doc
/// comment for the full reasoning (distinct 404/403, no admin-bypass since course moderation is P6-06's
/// job, Draft-only because P1-05 owns what deleting a non-draft course should mean).
/// </para>
/// </summary>
public sealed class DeleteCourseHandler(AppDbContext dbContext)
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบคอร์สนี้");
    private static readonly DomainError NotOwnerError = DomainError.Forbidden("คุณไม่มีสิทธิ์ลบคอร์สนี้");
    private static readonly DomainError NotDraftError = DomainError.Conflict("ลบได้เฉพาะคอร์สที่ยังเป็นฉบับร่าง (Draft) เท่านั้น");

    public async Task<Result> HandleAsync(Guid userId, Guid courseId, CancellationToken cancellationToken)
    {
        var course = await dbContext.Courses()
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken)
            .ConfigureAwait(false);

        if (course is null)
        {
            return Result.Failure(NotFoundError);
        }

        var instructorProfile = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (instructorProfile is null || course.InstructorId != instructorProfile.Id)
        {
            return Result.Failure(NotOwnerError);
        }

        if (course.Status != CourseStatus.Draft)
        {
            return Result.Failure(NotDraftError);
        }

        dbContext.Courses().Remove(course);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
