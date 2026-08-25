using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.ReorderCourseEpisodes;

public sealed class ReorderCourseEpisodesHandler(AppDbContext dbContext)
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบคอร์สหรือส่วน/บทหลักนี้");
    private static readonly DomainError NotOwnerError = DomainError.Forbidden("คุณไม่มีสิทธิ์แก้ไขคอร์สนี้");
    private static readonly DomainError NotDraftError = DomainError.Conflict("แก้ไขได้เฉพาะคอร์สที่ยังเป็นฉบับร่าง (Draft) หรือถูกปฏิเสธ (Rejected) เท่านั้น");

    public async Task<Result> HandleAsync(
        Guid userId, Guid courseId, Guid sectionId, ReorderCourseEpisodesCommand command, CancellationToken cancellationToken)
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

        var currentEpisodeIds = section.Episodes.Select(e => e.Id).ToHashSet();
        var requestedIds = command.Items.Select(i => i.EpisodeId).ToHashSet();

        if (!currentEpisodeIds.SetEquals(requestedIds))
        {
            return Result.Failure(DomainError.Validation("ต้องระบุบทเรียนทุกรายการภายใต้ส่วนนี้ให้ครบ ห้ามส่งบางส่วนหรือส่งบทเรียนของส่วนอื่น"));
        }

        // Order by requested SortOrder
        var orderedIds = command.Items.OrderBy(i => i.SortOrder).Select(i => i.EpisodeId).ToList();
        section.ReorderEpisodes(orderedIds);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
