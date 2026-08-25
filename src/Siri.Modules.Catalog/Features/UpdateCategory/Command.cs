namespace Siri.Modules.Catalog.Features.UpdateCategory;

/// <summary>Request payload for PUT /api/catalog/admin/categories/{id}. The target id comes from the
/// route (see Endpoint.cs), not this record — binds from the JSON request body.
/// <see cref="SortOrder"/> is deliberately absent: it is owned exclusively by
/// <c>Features.ReorderCategories</c>, so this endpoint and that one can never race to set it with
/// different intent.</summary>
public sealed record UpdateCategoryCommand(string Slug, string NameTh, string NameEn, string? IconKey, Guid? ParentId, bool IsActive);
