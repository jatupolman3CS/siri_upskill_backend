using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Infrastructure;

internal static class CourseGraphPersistence
{
    /// <summary>Saves a draft graph without colliding with its unique sibling-position indexes.</summary>
    public static async Task SaveAsync(AppDbContext db, Guid courseId, CancellationToken cancellationToken)
    {
        var sections = db.ChangeTracker.Entries<COURSE_SECTION>()
            .Where(entry => entry.Entity.CourseId == courseId && entry.State != EntityState.Added)
            .ToArray();
        var episodes = db.ChangeTracker.Entries<COURSE_EPISODE>()
            .Where(entry => entry.Entity.CourseId == courseId && entry.State != EntityState.Added)
            .ToArray();
        var reorderSections = sections.Any(entry => entry.Property(section => section.SortOrder).IsModified);
        var reorderEpisodes = episodes.Any(entry => entry.Property(episode => episode.SortOrder).IsModified);
        if (!reorderSections && !reorderEpisodes)
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return;
        }

        await using var transaction = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false)
            : null;

        // Free the non-negative positions before swapping them. The transaction keeps these
        // temporary positions invisible and rolls them back if optimistic concurrency fails.
        if (reorderSections)
        {
            await db.CourseSections().Where(section => section.CourseId == courseId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(section => section.SortOrder,
                    section => -section.SortOrder - 1), cancellationToken).ConfigureAwait(false);
            foreach (var entry in sections)
            {
                var position = entry.Property(section => section.SortOrder);
                position.OriginalValue = -position.OriginalValue - 1;
                if (entry.State != EntityState.Deleted) position.IsModified = true;
            }
        }

        if (reorderEpisodes)
        {
            await db.CourseEpisodes().Where(episode => episode.CourseId == courseId)
                .ExecuteUpdateAsync(setters => setters.SetProperty(episode => episode.SortOrder,
                    episode => -episode.SortOrder - 1), cancellationToken).ConfigureAwait(false);
            foreach (var entry in episodes)
            {
                var position = entry.Property(episode => episode.SortOrder);
                position.OriginalValue = -position.OriginalValue - 1;
                if (entry.State != EntityState.Deleted) position.IsModified = true;
            }
        }

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        if (transaction is not null) await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
    }
}
