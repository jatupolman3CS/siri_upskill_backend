namespace Siri.Modules.Catalog.Features.CreateCategory;

public sealed record CreateCategoryResponse(
    Guid Id,
    string Slug,
    string NameTh,
    string NameEn,
    string? IconKey,
    Guid? ParentId,
    int SortOrder,
    bool IsActive);
