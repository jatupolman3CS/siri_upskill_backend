using Microsoft.EntityFrameworkCore;
using Siri.Modules.Notification.Features.CreateAnnouncement;
using Siri.Modules.Notification.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Notification.Features.GetCourseAnnouncements;

public sealed class GetCourseAnnouncementsHandler(AppDbContext dbContext)
{
    public async Task<IReadOnlyList<AnnouncementResponse>> HandleAsync(
        Guid courseId,
        CancellationToken cancellationToken)
    {
        var announcements = await dbContext.Announcements()
            .AsNoTracking()
            .Where(a => a.CourseId == courseId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .Select(a => new AnnouncementResponse(
                a.Id,
                a.CourseId,
                a.InstructorId,
                a.Title,
                a.Body,
                a.SendEmail,
                a.ScheduledAtUtc,
                a.SentAtUtc,
                a.RecipientCount,
                a.DispatchStatus,
                a.CreatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return announcements;
    }
}
