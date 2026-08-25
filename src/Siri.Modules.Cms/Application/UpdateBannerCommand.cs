namespace Siri.Modules.Cms.Application;

/// <summary>Request payload for PUT /api/cms/admin/banners/{id}. The target id comes from the route, not
/// this record. <see cref="SortOrder"/> is deliberately absent — owned exclusively by
/// <c>ReorderBannersCommand</c>, same convention
/// <c>Siri.Modules.Catalog.Features.UpdateCategory.UpdateCategoryCommand</c>'s own doc comment describes.
/// See <see cref="CreateBannerCommand"/>'s own doc comment for why this includes fields <c>BANNER</c> has
/// no mutator for yet.</summary>
public sealed record UpdateBannerCommand(
    string Placement,
    string ImageUrl,
    string? MobileImageUrl,
    string? LinkUrl,
    string Title,
    DateTime? StartsAtUtc,
    DateTime? EndsAtUtc,
    bool IsActive);
