using Siri.Modules.Cms.Domain;

namespace Siri.Modules.Cms.Application;

/// <summary>
/// Persistence port for <see cref="MENU_ITEM"/> — see <see cref="IBannerRepository"/>'s own doc comment for
/// the general Repository+Service shape this follows (docs/DECISIONS.md D-17); <c>Infrastructure.MenuItemRepository</c>
/// is the only implementation.
/// </summary>
public interface IMenuItemRepository
{
    Task<MENU_ITEM?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Every menu item, flat — the caller assembles the tree in memory, mirroring
    /// <c>Siri.Modules.Catalog.Infrastructure.CategoryTreeAssembler</c>'s approach for the same
    /// self-referencing shape (see that class's own doc comment for why: an active child under an inactive
    /// parent must not leak into the public tree, which a flat SQL WHERE cannot express).</summary>
    Task<IReadOnlyList<MENU_ITEM>> GetAllAsync(CancellationToken cancellationToken);

    /// <summary>Direct children of <paramref name="parentId"/> (<c>null</c> = root-level siblings) — a
    /// future Reorder operation's validation and a future Delete's "has children" guard both need exactly
    /// this set, same shape <c>Siri.Modules.Catalog.Features.ReorderCategories.ReorderCategoriesHandler</c>
    /// loads for <c>Category</c>.</summary>
    Task<IReadOnlyList<MENU_ITEM>> GetChildrenAsync(Guid? parentId, CancellationToken cancellationToken);

    void Add(MENU_ITEM menuItem);

    void Remove(MENU_ITEM menuItem);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
