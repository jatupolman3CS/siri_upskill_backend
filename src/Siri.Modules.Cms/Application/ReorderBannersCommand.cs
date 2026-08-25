namespace Siri.Modules.Cms.Application;

/// <summary>
/// Request payload for POST /api/cms/admin/banners/reorder. Must name every current banner in one
/// placement (a future handler resolves "which placement" itself from the loaded banners — never trusts a
/// client-claimed placement) — mirrors
/// <c>Siri.Modules.Catalog.Features.ReorderCategories.ReorderCategoriesCommand</c>'s own doc comment
/// exactly, with <see cref="Domain.BANNER.PLACEMENT"/> standing in for <c>Category.ParentId</c> as the
/// grouping key.
/// </summary>
public sealed record ReorderBannersCommand(IReadOnlyList<ReorderBannerItem> Items);

public sealed record ReorderBannerItem(Guid BannerId, int SortOrder);
