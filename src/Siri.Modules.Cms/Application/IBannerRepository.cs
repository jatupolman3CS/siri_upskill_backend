using Siri.Modules.Cms.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Cms.Application;

/// <summary>
/// Persistence port for <see cref="BANNER"/> (docs/DECISIONS.md D-17's Repository+Service pattern —
/// <c>Application/</c> depends on this abstraction only, never <c>Siri.Persistence.AppDbContext</c> or EF
/// Core directly; <c>Infrastructure.BannerRepository</c> is the only implementation). Interface name is NOT
/// uppercased (.claude/rules/backend.md: "Repository/Service/DTO/interface/namespace names are NOT
/// uppercased — IPostRepository, not IPOSTRepository") — only the entity type it operates on is.
/// <para>
/// Follows a "repository owns its own save" shape (a <see cref="SaveChangesAsync"/> method here, rather
/// than a separate Unit-of-Work abstraction) — the simplest shape that still keeps
/// <c>Application.BannerService</c> free of any direct EF Core dependency, and docs/DECISIONS.md D-17 only
/// asked for Repository+Service, not a third UnitOfWork layer on top. Every member is unimplemented in this
/// scaffold pass — see <c>Infrastructure.BannerRepository</c>'s own doc comment for why (and for why its
/// constructor is not a primary constructor despite this repo's usual style).
/// </para>
/// </summary>
public interface IBannerRepository
{
    Task<BANNER?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Every banner in this placement, regardless of <see cref="BANNER.IS_ACTIVE"/> or schedule
    /// window — the full current sibling set, e.g. for a future Reorder operation's validation (mirrors
    /// <c>Siri.Modules.Catalog.Features.ReorderCategories.ReorderCategoriesHandler</c>'s "load the complete
    /// current sibling list" step).</summary>
    Task<IReadOnlyList<BANNER>> GetByPlacementAsync(string placement, CancellationToken cancellationToken);

    /// <summary>Active banners in this placement whose schedule window currently includes
    /// <paramref name="nowUtc"/>, in display order — the public consumption read (e.g. a future homepage
    /// endpoint, docs/TASKS.md P6-25).</summary>
    Task<IReadOnlyList<BANNER>> GetActiveByPlacementAsync(string placement, DateTime nowUtc, CancellationToken cancellationToken);

    /// <summary>Every banner across every placement, offset-paginated — the admin list
    /// (.claude/rules/database.md: "รายการที่โตได้ต้อง paginate เสมอ ... offset ได้สำหรับ admin table").</summary>
    Task<PagedResult<BANNER>> GetPagedAsync(int page, int pageSize, CancellationToken cancellationToken);

    void Add(BANNER banner);

    void Remove(BANNER banner);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
