namespace Siri.Modules.Commerce.Domain;

/// <summary>
/// What <see cref="CART_ITEM.REF_ID"/> points at. Only these two purchasable catalog concepts exist at
/// this scaffolding stage (Courses via <see cref="Siri.Modules.Catalog"/>, Bundles via this module's own
/// <see cref="BUNDLE"/>) — not specified by docs/DATABASE.md's terse "ItemType, RefId" sketch, so this
/// exact member set is an inferred-but-reasonable scaffold decision, not a confirmed spec. Enum TYPE and
/// MEMBERS stay normal PascalCase — same reasoning as <see cref="OrderStatus"/>.
/// </summary>
public enum CartItemType { Course, Bundle }
