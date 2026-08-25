namespace Siri.Modules.Catalog.Features.ReorderCategories;

/// <summary>Request payload for POST /api/catalog/admin/categories/reorder. Must name every current
/// sibling of one parent (the handler resolves "one parent" itself from the loaded categories — never
/// trusts a client-claimed parent id) — see <c>Handler.cs</c>'s doc comment for why partial batches
/// are rejected rather than applied.</summary>
public sealed record ReorderCategoriesCommand(IReadOnlyList<ReorderCategoryItem> Items);

public sealed record ReorderCategoryItem(Guid CategoryId, int SortOrder);
