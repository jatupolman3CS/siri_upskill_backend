using Microsoft.EntityFrameworkCore;
using Siri.Modules.Notification.Features.CreateAnnouncement;
using Siri.Modules.Notification.Infrastructure;
using Siri.Persistence;

namespace Siri.Modules.Notification.Features.GetInstructorAnnouncements;

public sealed class GetInstructorAnnouncementsHandler(AppDbContext dbContext)
{
    public async Task<IReadOnlyList<AnnouncementResponse>> HandleAsync(
        Guid instructorId,
        CancellationToken cancellationToken)
    {
        var announcements = await dbContext.Announcements()
            .AsNoTracking()
            .Where(a => a.InstructorId == instructorId)
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
                a.CreatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return announcements;
    }
}
