using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.UpdateCourse;

/// <summary>
/// Updates a draft course's editable fields (title/subtitle/description/category/level/language/
/// thumbnail/pricing/access-duration/SEO) — combined into one PUT, same "one endpoint, several related
/// fields" shape <c>UpdateCategoryHandler</c> already established. Slug is not editable here (out of this
/// task's scope — stays stable once <c>CreateCourseHandler</c> generates it; changing the title does not
/// regenerate it).
/// <para>
/// <b>Ownership check</b> — <see cref="Course.InstructorId"/> must equal the caller's own
/// <c>InstructorProfile.Id</c>. Deliberately distinct 404 (course doesn't exist) vs 403 (exists, but
/// isn't the caller's) responses, unlike <c>RevokeSessionHandler</c>'s collapsed-into-one-404 approach —
/// a course id isn't a session-hijacking-adjacent secret (once published, course existence is public
/// catalog data anyway), so there is no anti-enumeration reason to hide the distinction here. No
/// admin-bypass: course moderation for admins is task P6-06's ("Admin ops API: course moderation"), an
/// explicitly separate, later task — building it into this handler now would be scope creep into work
/// this codebase already plans to do properly, later.
/// </para>
/// <para>
/// <b>Draft-only</b>: rejects with a conflict once <see cref="Course.Status"/> has moved past
/// <see cref="CourseStatus.Draft"/> — task P1-05 ("Publish workflow") owns what editing an InReview/
/// Published/Rejected course should mean, which does not exist yet; this task's own name ("Course CRUD
/// (draft)") scopes it to drafts only.
/// </para>
/// </summary>
public sealed class UpdateCourseHandler(AppDbContext dbContext)
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบคอร์สนี้");
    private static readonly DomainError NotOwnerError = DomainError.Forbidden("คุณไม่มีสิทธิ์แก้ไขคอร์สนี้");
    private static readonly DomainError NotDraftError = DomainError.Conflict("แก้ไขได้เฉพาะคอร์สที่ยังเป็นฉบับร่าง (Draft) เท่านั้น");

    public async Task<Result<UpdateCourseResponse>> HandleAsync(
        Guid userId, Guid courseId, UpdateCourseCommand command, CancellationToken cancellationToken)
    {
        var course = await dbContext.Courses()
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken)
            .ConfigureAwait(false);

        if (course is null)
        {
            return Result.Failure<UpdateCourseResponse>(NotFoundError);
        }

        var instructorProfile = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (instructorProfile is null || course.InstructorId != instructorProfile.Id)
        {
            return Result.Failure<UpdateCourseResponse>(NotOwnerError);
        }

        if (course.Status != CourseStatus.Draft)
        {
            return Result.Failure<UpdateCourseResponse>(NotDraftError);
        }

        var categoryExists = await dbContext.Categories()
            .AsNoTracking()
            .AnyAsync(c => c.Id == command.CategoryId, cancellationToken)
            .ConfigureAwait(false);

        if (!categoryExists)
        {
            return Result.Failure<UpdateCourseResponse>(DomainError.NotFound("ไม่พบหมวดหมู่ที่ระบุ"));
        }

        course.UpdateBasicInfo(command.Title, command.Subtitle, command.Description);
        course.SetCategory(command.CategoryId);
        course.SetLevel(command.Level);
        course.SetLanguage(command.Language);
        course.SetThumbnail(command.ThumbnailUrl);
        course.SetPricing(command.Price, command.ComparePrice);
        course.SetAccessDuration(command.AccessDurationDays);
        course.SetSeo(command.SeoTitle, command.SeoDescription);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return ToResponse(course);
    }

    private static UpdateCourseResponse ToResponse(Course course) =>
        new(course.Id, course.Slug, course.Title, course.Subtitle, course.Description, course.InstructorId,
            course.CategoryId, course.Level, course.Language, course.ThumbnailUrl, course.Price, course.ComparePrice,
            course.Currency, course.AccessDurationDays, course.Status, course.SeoTitle, course.SeoDescription);
}
