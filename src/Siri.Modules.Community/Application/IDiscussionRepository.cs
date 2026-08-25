using Siri.Modules.Community.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Community.Application;

/// <summary>
/// Persistence abstraction for <see cref="DISCUSSION"/>, implemented by
/// <see cref="Infrastructure.DiscussionRepository"/>. <see cref="DiscussionService"/> depends on this
/// interface only, never on <c>Siri.Persistence.AppDbContext</c>/EF Core types directly — the D-17
/// Repository+Service split (docs/DECISIONS.md), unlike this repo's other (vertical-slice) modules where
/// the feature Handler talks to <c>AppDbContext</c> directly.
/// </summary>
public interface IDiscussionRepository
{
    Task<DISCUSSION?> GetByIdAsync(Guid discussionId, CancellationToken cancellationToken);

    /// <summary>Backs the classroom Q&amp;A tab — offset-paginated, newest-first, matching the
    /// <c>IX(EpisodeId, Status, CreatedAtUtc)</c> index docs/DATABASE.md's community section specifies
    /// (see <see cref="Infrastructure.DiscussionConfiguration"/>).</summary>
    Task<PagedResult<DISCUSSION>> ListByEpisodeAsync(Guid episodeId, int page, int pageSize, CancellationToken cancellationToken);

    void Add(DISCUSSION discussion);

    /// <summary>Soft-delete — <see cref="DISCUSSION"/> implements <c>ISoftDelete</c>, so
    /// <c>AuditableEntityInterceptor</c> turns this into an <c>IsDeleted</c> flag update automatically on
    /// the next <see cref="SaveChangesAsync"/>, same as every other soft-deletable entity in this codebase
    /// (e.g. <c>Siri.Modules.Catalog.Domain.Course</c>) — no explicit domain "delete" method needed.</summary>
    void Remove(DISCUSSION discussion);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
