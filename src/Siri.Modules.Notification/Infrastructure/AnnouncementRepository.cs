using Microsoft.EntityFrameworkCore;
using Siri.Modules.Notification.Application;
using Siri.Modules.Notification.Domain;
using Siri.Persistence;

namespace Siri.Modules.Notification.Infrastructure;

public sealed class AnnouncementRepository(AppDbContext dbContext) : IAnnouncementRepository
{
    public Task<ANNOUNCEMENT?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.Announcements().FirstOrDefaultAsync(a => a.Id == id, cancellationToken);

    public async Task<IReadOnlyList<ANNOUNCEMENT>> GetByCourseIdAsync(Guid courseId, CancellationToken cancellationToken) =>
        await dbContext.Announcements()
            .Where(a => a.CourseId == courseId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task<IReadOnlyList<ANNOUNCEMENT>> GetByInstructorIdAsync(Guid instructorId, CancellationToken cancellationToken) =>
        await dbContext.Announcements()
            .Where(a => a.InstructorId == instructorId)
            .OrderByDescending(a => a.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task AddAsync(ANNOUNCEMENT announcement, CancellationToken cancellationToken)
    {
        dbContext.Announcements().Add(announcement);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(ANNOUNCEMENT announcement, CancellationToken cancellationToken)
    {
        dbContext.Announcements().Update(announcement);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
