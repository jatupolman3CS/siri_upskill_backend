namespace Siri.Modules.Cms.Application;

/// <summary>
/// Request payload for PUT /api/cms/admin/menu-items/{id}. The target id comes from the route, not this
/// record. <see cref="Domain.MENU_ITEM.SORT_ORDER"/> is deliberately absent, same "owned exclusively by a
/// future Reorder operation" convention <see cref="UpdateBannerCommand"/>'s own doc comment describes for
/// <c>BANNER.SortOrder</c> — see <see cref="MenuItemService"/>'s own doc comment for why this module does
/// not add that Reorder operation for <c>MENU_ITEM</c> in this pass. <see cref="ParentId"/> IS included
/// (moving an item to a different parent is a regular edit here, not a reorder), mirroring how
/// <see cref="UpdateBannerCommand.Placement"/> is likewise editable via a plain update rather than a
/// dedicated "move" operation.
/// </summary>
public sealed record UpdateMenuItemCommand(Guid? ParentId, string Label, string Url, bool IsActive);
