using Siri.Modules.Cms.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Cms.Application;

/// <summary>
/// Persistence port for <see cref="POST"/> — see <see cref="IBannerRepository"/>'s own doc comment for the
/// general Repository+Service shape this follows (docs/DECISIONS.md D-17); <c>Infrastructure.PostRepository</c>
/// is the only implementation.
/// </summary>
public interface IPostRepository
{
    Task<POST?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Published-only lookup by slug — the public detail read. Draft/Archived posts (and
    /// nonexistent slugs) are meant to resolve identically to "not found" here, the same Published-only-by-
    /// slug convention <c>Siri.Modules.Catalog.Features.GetCourseDetail.GetCourseDetailHandler</c> uses so a
    /// draft never leaks even its existence.</summary>
    Task<POST?> GetPublishedBySlugAsync(string slug, CancellationToken cancellationToken);

    /// <summary>Whether <paramref name="slug"/> is already taken by another post (soft-deleted posts'
    /// slugs are free to reuse — see <c>Infrastructure.PostConfiguration</c>'s filtered unique index).
    /// <paramref name="excludePostId"/> lets an Update check exclude the post being edited.</summary>
    Task<bool> SlugExistsAsync(string slug, Guid? excludePostId, CancellationToken cancellationToken);

    /// <summary>Every post regardless of status, offset-paginated — the admin list
    /// (.claude/rules/database.md: "รายการที่โตได้ต้อง paginate เสมอ ... offset ได้สำหรับ admin table").</summary>
    Task<PagedResult<POST>> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken);

    /// <summary>Published-only posts, offset-paginated — the public blog index (mirrors
    /// <see cref="GetPublishedBySlugAsync"/>'s Published-only convention). Added during the
    /// Application-layer scaffold pass (not part of the original Domain+Infrastructure pass this interface
    /// shipped with) because the public blog list needs a dedicated filtered+paginated query that
    /// <see cref="GetPagedAsync"/> cannot answer without breaking pagination correctness — that method
    /// deliberately returns every status for the admin list, so filtering its output down to Published in
    /// the caller would report a <c>PagedResult&lt;T&gt;.TotalCount</c> that still counts non-Published
    /// rows.</summary>
    Task<PagedResult<POST>> GetPublishedPagedAsync(int page, int pageSize, CancellationToken cancellationToken);

    void Add(POST post);

    void Remove(POST post);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
