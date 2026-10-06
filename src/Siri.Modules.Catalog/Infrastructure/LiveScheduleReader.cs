using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Infrastructure;

/// <summary>Read-side implementation of <see cref="ILiveScheduleReader"/> — queries
/// <see cref="COURSE_LIVE_SESSION"/> directly (via <see cref="AppDbContextCatalogExtensions.CourseLiveSessions"/>)
/// and maps into <see cref="Contracts"/> records so callers outside this module never see
/// <see cref="Domain.COURSE_LIVE_SESSION"/>/<see cref="Domain.CourseLiveSessionStatus"/> — same
/// module-boundary discipline <see cref="Contracts.CatalogPriceContract"/> already follows.</summary>
public sealed class LiveScheduleReader(AppDbContext dbContext) : ILiveScheduleReader
{
    public async Task<IReadOnlyList<LiveSessionInfo>> GetSessionsForCourseAsync(Guid courseId, CancellationToken cancellationToken)
    {
        return await dbContext.CourseLiveSessions()
            .AsNoTracking()
            .Where(s => s.CourseId == courseId)
            .OrderBy(s => s.StartsAtUtc)
            .Select(s => ToLiveSessionInfo(s))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<LiveSessionInfo>> GetUpcomingSessionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken)
    {
        return await dbContext.CourseLiveSessions()
            .AsNoTracking()
            .Where(s => s.Status == CourseLiveSessionStatus.Scheduled && s.StartsAtUtc >= fromUtc && s.StartsAtUtc < toUtc)
            .OrderBy(s => s.StartsAtUtc)
            .Select(s => ToLiveSessionInfo(s))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<LiveSessionInfo?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        return await dbContext.CourseLiveSessions()
            .AsNoTracking()
            .Where(s => s.Id == sessionId)
            .Select(s => ToLiveSessionInfo(s))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    private static LiveSessionInfo ToLiveSessionInfo(COURSE_LIVE_SESSION session) => new(
        session.Id,
        session.CourseId,
        session.Title,
        session.StartsAtUtc,
        session.EndsAtUtc,
        session.Status == CourseLiveSessionStatus.Cancelled ? LiveSessionStatus.Cancelled : LiveSessionStatus.Scheduled,
        session.RecordingEpisodeId);
}
