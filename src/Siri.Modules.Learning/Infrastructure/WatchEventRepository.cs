using Microsoft.EntityFrameworkCore;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Domain;
using Siri.Persistence;

namespace Siri.Modules.Learning.Infrastructure;

/// <summary>
/// EF Core-backed <see cref="IWatchEventRepository"/> — loads/persists <see cref="WATCH_EVENT"/> via the
/// shared <see cref="AppDbContext"/> through <see cref="AppDbContextLearningExtensions.WatchEvents"/>.
/// <para>
/// Implemented for real as of 2026-08-24 (the old SCAFFOLD note was stale). Deliberately a
/// traditional constructor, not a primary constructor — see <c>EnrollmentRepository</c>'s constructor's
/// own doc comment for why (same reasoning, not repeated here).
/// </para>
/// </summary>
public sealed class WatchEventRepository(AppDbContext dbContext) : IWatchEventRepository
{
    public IQueryable<WATCH_EVENT> Query() => dbContext.WatchEvents().AsNoTracking();

    public void Append(WATCH_EVENT watchEvent)
    {
        ArgumentNullException.ThrowIfNull(watchEvent);
        dbContext.WatchEvents().Add(watchEvent);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
