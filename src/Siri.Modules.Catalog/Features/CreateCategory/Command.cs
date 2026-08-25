namespace Siri.Modules.Catalog.Features.CreateCategory;

/// <summary>Request payload for POST /api/catalog/admin/categories. Binds from the JSON request body.
/// <see cref="SortOrder"/> is deliberately not a field here — the handler computes "append to end of
/// the new siblings" itself; the admin creating a category has no reason to know sibling counts.</summary>
public sealed record CreateCategoryCommand(string Slug, string NameTh, string NameEn, string? IconKey, Guid? ParentId);
