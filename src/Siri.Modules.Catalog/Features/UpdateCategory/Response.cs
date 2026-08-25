namespace Siri.Modules.Catalog.Features.UpdateCategory;

public sealed record UpdateCategoryResponse(
    Guid Id,
    string Slug,
    string NameTh,
    string NameEn,
    string? IconKey,
    Guid? ParentId,
    int SortOrder,
    bool IsActive);
