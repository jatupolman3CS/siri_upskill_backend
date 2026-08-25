namespace Siri.Modules.Cms.Application;

/// <summary>
/// Request payload for POST /api/cms/admin/menu-items. Binds from the JSON request body.
/// <see cref="Domain.MENU_ITEM.SORT_ORDER"/> is deliberately not a field here — a future handler computes
/// "append to end of this parent's current children" itself, same convention
/// <see cref="CreateBannerCommand"/>'s own doc comment describes for <c>BANNER.SORT_ORDER</c> (here,
/// <see cref="ParentId"/> is the sibling-grouping key <c>Placement</c> plays for <c>BANNER</c>).
/// </summary>
public sealed record CreateMenuItemCommand(Guid? ParentId, string Label, string Url);
