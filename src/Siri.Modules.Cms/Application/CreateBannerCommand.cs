namespace Siri.Modules.Cms.Application;

/// <summary>
/// Request payload for POST /api/cms/admin/banners. Binds from the JSON request body.
/// <see cref="SortOrder"/> is deliberately not a field here — a future handler is meant to compute "append
/// to end of this placement's current banners" itself, same convention
/// <c>Siri.Modules.Catalog.Features.CreateCategory.CreateCategoryCommand</c>'s own doc comment describes
/// for <c>Category</c>.
/// <para>
/// Carries every field the admin banner form needs, including ones <see cref="Domain.BANNER.Create"/> does
/// not yet accept as parameters (<see cref="MobileImageUrl"/>/<see cref="LinkUrl"/>/<see cref="StartsAtUtc"/>/
/// <see cref="EndsAtUtc"/>) — wiring those through <c>Application.BannerService.CreateAsync</c> requires
/// adding the corresponding Set* mutators to <c>BANNER</c> first, which is exactly the "later task" gap
/// <c>BANNER.Create</c>'s own doc comment already calls out. The API contract (this record) is scaffolded
/// complete now; the domain-side wiring to fully honor it is not.
/// </para>
/// </summary>
public sealed record CreateBannerCommand(
    string Placement,
    string ImageUrl,
    string? MobileImageUrl,
    string? LinkUrl,
    string Title,
    DateTime? StartsAtUtc,
    DateTime? EndsAtUtc);
