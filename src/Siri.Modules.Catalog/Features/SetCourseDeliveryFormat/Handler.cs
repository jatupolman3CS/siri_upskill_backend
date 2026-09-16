using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.SetCourseDeliveryFormat;

public sealed class SetCourseDeliveryFormatHandler(
    AppDbContext dbContext,
    IOutputCacheStore outputCacheStore)
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบคอร์สนี้");
    private static readonly DomainError NotOwnerError = DomainError.Forbidden("คุณไม่มีสิทธิ์แก้ไขคอร์สนี้");

    public async Task<Result<SetCourseDeliveryFormatResponse>> HandleAsync(
        Guid userId,
        Guid courseId,
        SetCourseDeliveryFormatCommand command,
        CancellationToken cancellationToken)
    {
        var course = await dbContext.Courses()
            .Include(c => c.LiveSessions)
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken)
            .ConfigureAwait(false);

        if (course is null)
        {
            return Result.Failure<SetCourseDeliveryFormatResponse>(NotFoundError);
        }

        var instructorProfile = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (instructorProfile is null || course.InstructorId != instructorProfile.Id)
        {
            return Result.Failure<SetCourseDeliveryFormatResponse>(NotOwnerError);
        }

        if (course.Status == CourseStatus.Archived)
        {
            return Result.Failure<SetCourseDeliveryFormatResponse>(
                DomainError.Conflict($"Cannot change delivery format of a course in {course.Status} status."));
        }

        if (command.DeliveryFormat == DeliveryFormat.OnDemand
            && course.LiveSessions.Any(s => s.Status == CourseLiveSessionStatus.Scheduled))
        {
            return Result.Failure<SetCourseDeliveryFormatResponse>(
                DomainError.Conflict("ยกเลิกทุกคาบสอนสดก่อนเปลี่ยนกลับเป็น OnDemand"));
        }

        try
        {
            course.SetDeliveryFormat(command.DeliveryFormat);
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<SetCourseDeliveryFormatResponse>(DomainError.Conflict(ex.Message));
        }

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (course.Status == CourseStatus.Published)
        {
            await outputCacheStore.EvictByTagAsync(CourseOutputCache.Tag, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success(new SetCourseDeliveryFormatResponse(course.Id, course.DeliveryFormat));
    }
}
