using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.DeleteCourseSection;

public sealed class DeleteCourseSectionHandler(AppDbContext dbContext, TeachingMaterialStorage materialStorage)
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบคอร์สหรือส่วน/บทหลักนี้");
    private static readonly DomainError NotOwnerError = DomainError.Forbidden("คุณไม่มีสิทธิ์แก้ไขคอร์สนี้");
    private static readonly DomainError NotDraftError = DomainError.Conflict("แก้ไขได้เฉพาะคอร์สที่ยังเป็นฉบับร่าง (Draft) หรือถูกปฏิเสธ (Rejected) เท่านั้น");

    public async Task<Result> HandleAsync(Guid userId, Guid courseId, Guid sectionId, CancellationToken cancellationToken)
    {
        var course = await dbContext.Courses()
            .Include(c => c.Sections).ThenInclude(s => s.Episodes)
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

        var section = course.Sections.FirstOrDefault(s => s.Id == sectionId);
        if (section is null)
        {
            return Result.Failure(NotFoundError);
        }

        // The attachment rows go with the section's episodes (FK cascade); their R2 objects don't, so note the keys first.
        var episodeIds = section.Episodes.Select(e => e.Id).ToList();
        var storageKeys = await dbContext.EpisodeAttachments()
            .AsNoTracking()
            .Where(a => episodeIds.Contains(a.EpisodeId))
            .Select(a => a.StorageKey)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        course.RemoveSection(sectionId);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        await materialStorage.DeleteQuietlyAsync(storageKeys, cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
