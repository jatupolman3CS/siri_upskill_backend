namespace Siri.Modules.Cms.Application;

/// <summary>
/// Wire-shape projection of <see cref="Domain.MENU_ITEM"/>. Ordinary PascalCase properties — DTOs are
/// explicitly exempt from docs/DECISIONS.md D-17's UPPERCASE rule (.claude/rules/backend.md:
/// "Repository/Service/DTO/interface/namespace names are NOT uppercased"), same as
/// <see cref="BannerResponse"/>.
/// </summary>
public sealed record MenuItemResponse(
    Guid Id,
    Guid? ParentId,
    string Label,
    string Url,
    int SortOrder,
    bool IsActive);
