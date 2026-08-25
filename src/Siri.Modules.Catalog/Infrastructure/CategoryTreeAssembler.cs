using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Infrastructure;

/// <summary>One node of an assembled category tree — the shared shape both the public
/// (<c>Features/GetCategoryTree</c>) and admin (<c>Features/GetAdminCategoryTree</c>) reads return, so
/// the two can never drift on field shape.</summary>
public sealed record CategoryTreeNode(
    Guid Id,
    string Slug,
    string NameTh,
    string NameEn,
    string? IconKey,
    int SortOrder,
    bool IsActive,
    IReadOnlyList<CategoryTreeNode> Children);

/// <summary>
/// Builds a <see cref="CategoryTreeNode"/> tree from one flat <see cref="Category"/> list. Both tree
/// reads query every category in a single <c>.AsNoTracking()</c> call regardless of status and hand
/// the whole list here — filtering at the query level instead would be wrong: an *active* child whose
/// *parent* is inactive would end up orphaned (either silently promoted to root, or requiring the
/// query itself to somehow know about ancestor status, which SQL can't express in one flat WHERE).
/// <see cref="Assemble"/> instead prunes top-down after building the full tree in memory: a node whose
/// own <see cref="Category.IsActive"/> is false is dropped, and — because dropping it also drops the
/// recursive call into its children — so is everything under it, regardless of those descendants' own
/// <see cref="Category.IsActive"/> value.
/// </summary>
public static class CategoryTreeAssembler
{
    public static IReadOnlyList<CategoryTreeNode> Assemble(IReadOnlyList<Category> flat, bool includeInactive)
    {
        // ILookup, not Dictionary<Guid?, ...>: Dictionary/ToDictionary require `TKey : notnull`, which
        // a nullable value type (Guid? — null means "root") cannot satisfy under nullable-reference
        // checking, even though it is a perfectly ordinary lookup key at runtime. ToLookup has no such
        // constraint, and its indexer already returns an empty sequence for an absent key (a category
        // with no children), so there is no separate "key missing" branch to write either.
        var childrenByParent = flat.ToLookup(c => c.ParentId);

        return BuildLevel(null, childrenByParent, includeInactive);
    }

    private static List<CategoryTreeNode> BuildLevel(
        Guid? parentId,
        ILookup<Guid?, Category> childrenByParent,
        bool includeInactive)
    {
        var siblings = childrenByParent[parentId].OrderBy(c => c.SortOrder);
        var nodes = new List<CategoryTreeNode>();

        foreach (var category in siblings)
        {
            if (!includeInactive && !category.IsActive)
            {
                // Prune here — do not recurse into this category's children at all, so an active
                // descendant of an inactive category never leaks into the public tree.
                continue;
            }

            var children = BuildLevel(category.Id, childrenByParent, includeInactive);

            nodes.Add(new CategoryTreeNode(
                category.Id,
                category.Slug,
                category.NameTh,
                category.NameEn,
                category.IconKey,
                category.SortOrder,
                category.IsActive,
                children));
        }

        return nodes;
    }
}
