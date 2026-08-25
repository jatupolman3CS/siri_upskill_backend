namespace Siri.Modules.Cms.Application;

/// <summary>
/// Wire-shape projection of <see cref="Domain.BANNER"/>. Ordinary PascalCase properties — DTOs are
/// explicitly exempt from docs/DECISIONS.md D-17's UPPERCASE rule (.claude/rules/backend.md:
/// "Repository/Service/DTO/interface/namespace names are NOT uppercased"), so this looks like any other
/// response record in this codebase and serializes to the same camelCase JSON shape every other endpoint
/// already produces.
/// </summary>
public sealed record BannerResponse(
    Guid Id,
    string Placement,
    string ImageUrl,
    string? MobileImageUrl,
    string? LinkUrl,
    string Title,
    int SortOrder,
    DateTime? StartsAtUtc,
    DateTime? EndsAtUtc,
    bool IsActive);
