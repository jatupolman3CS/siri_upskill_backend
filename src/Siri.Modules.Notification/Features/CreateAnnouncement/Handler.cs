using Siri.Modules.Notification.Contracts;
using Siri.Modules.Notification.Domain;
using Siri.Modules.Notification.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Notification.Features.CreateAnnouncement;

public sealed class CreateAnnouncementHandler(
    AppDbContext dbContext,
    ICourseOwnershipVerifier ownershipVerifier,
    IClock clock)
{
    public async Task<Result<AnnouncementResponse>> HandleAsync(
        Guid instructorUserId,
        CreateAnnouncementCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var isOwner = await ownershipVerifier.IsInstructorOwnerOfCourseAsync(
            command.CourseId, instructorUserId, cancellationToken).ConfigureAwait(false);

        if (!isOwner)
        {
            return Result.Failure<AnnouncementResponse>(
                DomainError.Forbidden("คุณไม่ใช่เจ้าของคอร์สนี้ ไม่สามารถสร้างประกาศได้"));
        }

        var announcement = Announcement.Create(
            command.CourseId,
            instructorUserId,
            command.Title,
            command.Body,
            command.SendEmail,
            command.ScheduledAtUtc,
            clock);

        dbContext.Announcements().Add(announcement);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new AnnouncementResponse(
            announcement.Id,
            announcement.CourseId,
            announcement.InstructorId,
            announcement.Title,
            announcement.Body,
            announcement.SendEmail,
            announcement.ScheduledAtUtc,
            announcement.SentAtUtc,
            announcement.RecipientCount,
            announcement.CreatedAtUtc));
    }
}
