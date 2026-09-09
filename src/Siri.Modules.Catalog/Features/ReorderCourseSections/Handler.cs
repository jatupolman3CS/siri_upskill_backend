using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.ReorderCourseSections;

public sealed class ReorderCourseSectionsHandler(AppDbContext dbContext)
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบคอร์สนี้");
    private static readonly DomainError NotOwnerError = DomainError.Forbidden("คุณไม่มีสิทธิ์แก้ไขคอร์สนี้");
    private static readonly DomainError NotDraftError = DomainError.Conflict("แก้ไขได้เฉพาะคอร์สที่ยังเป็นฉบับร่าง (Draft) หรือถูกปฏิเสธ (Rejected) เท่านั้น");

    public async Task<Result> HandleAsync(
        Guid userId, Guid courseId, ReorderCourseSectionsCommand command, CancellationToken cancellationToken)
    {
        var course = await dbContext.Courses()
            .Include(c => c.Sections)
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

        if (course.Status is not (CourseStatus.Draft or CourseStatus.Rejected))
        {
            return Result.Failure(NotDraftError);
        }

        var currentSectionIds = course.Sections.Select(s => s.Id).ToHashSet();
        var requestedIds = command.Items.Select(i => i.SectionId).ToHashSet();

        if (!currentSectionIds.SetEquals(requestedIds))
        {
            return Result.Failure(DomainError.Validation("ต้องระบุส่วน/บทหลักทุกรายการภายใต้คอร์สนี้ให้ครบ ห้ามส่งบางส่วนหรือส่งส่วนของคอร์สอื่น"));
        }

        // Order by requested SortOrder
        var orderedIds = command.Items.OrderBy(i => i.SortOrder).Select(i => i.SectionId).ToList();
        course.ReorderSections(orderedIds);

        await CourseGraphPersistence.SaveAsync(dbContext, course.Id, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
