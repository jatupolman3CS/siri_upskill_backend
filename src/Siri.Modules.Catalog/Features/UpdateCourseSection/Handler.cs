using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.CreateCourseSection;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.UpdateCourseSection;

public sealed class UpdateCourseSectionHandler(AppDbContext dbContext)
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบคอร์สหรือส่วน/บทหลักนี้");
    private static readonly DomainError NotOwnerError = DomainError.Forbidden("คุณไม่มีสิทธิ์แก้ไขคอร์สนี้");
    private static readonly DomainError NotDraftError = DomainError.Conflict("แก้ไขได้เฉพาะคอร์สที่ยังเป็นฉบับร่าง (Draft) หรือถูกปฏิเสธ (Rejected) เท่านั้น");

    public async Task<Result<CourseSectionResponse>> HandleAsync(
        Guid userId, Guid courseId, Guid sectionId, UpdateCourseSectionCommand command, CancellationToken cancellationToken)
    {
        var course = await dbContext.Courses()
            .Include(c => c.Sections)
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken)
            .ConfigureAwait(false);

        if (course is null)
        {
            return Result.Failure<CourseSectionResponse>(NotFoundError);
        }

        var instructorProfile = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (instructorProfile is null || course.InstructorId != instructorProfile.Id)
        {
            return Result.Failure<CourseSectionResponse>(NotOwnerError);
        }

        if (course.Status is not (CourseStatus.Draft or CourseStatus.Rejected))
        {
            return Result.Failure<CourseSectionResponse>(NotDraftError);
        }

        var section = course.Sections.FirstOrDefault(s => s.Id == sectionId);
        if (section is null)
        {
            return Result.Failure<CourseSectionResponse>(NotFoundError);
        }

        section.Rename(command.Title);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new CourseSectionResponse(section.Id, section.CourseId, section.Title, section.SortOrder));
    }
}
