using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Cms.Domain;

/// <summary>
/// A promotional banner shown in a fixed UI slot (<see cref="PLACEMENT"/>) — e.g. the homepage hero —
/// admin-managed without a code deploy (docs/REQUIREMENTS.md AD-01: "จัด banner หน้าแรก"; docs/DATABASE.md's
/// "cms" section: "Banners(Id, Placement, ImageUrl, MobileImageUrl, LinkUrl, Title, SortOrder, StartsAtUtc,
/// EndsAtUtc, IsActive)").
/// <para>
/// <b>UPPERCASE naming exception</b> (docs/DECISIONS.md D-17) — this is the first of this module's four
/// entities, so the full reasoning lives here; <c>MENU_ITEM</c>/<c>POST</c>/<c>REDIRECT</c> cross-reference
/// this comment instead of repeating it. The project owner decided the 7 brand-new Repository+Service
/// modules (Commerce/Media/Learning/Payout/Cms/Community/Analytics) use UPPERCASE for entity class names,
/// entity property names, and the DB tables/columns they map to — a deliberate, module-scoped deviation
/// from this repo's normal PascalCase convention (.claude/rules/database.md). Identity/Catalog/Notification
/// are untouched and stay PascalCase. The <see cref="IAuditable"/> block below is the one exception within
/// the exception: <see cref="CreatedAtUtc"/>/<see cref="CreatedBy"/>/<see cref="UpdatedAtUtc"/>/
/// <see cref="UpdatedBy"/> stay ordinary PascalCase C# property names regardless of which naming
/// convention the rest of the entity uses, because <c>Siri.Persistence.Interceptors.AuditableEntityInterceptor</c>
/// looks them up via <c>nameof(IAuditable.CreatedAtUtc)</c> — a hardcoded C#-member-name string lookup
/// shared by every module, this one included. Renaming those four properties would make every
/// <c>SaveChangesAsync</c> throw <see cref="InvalidOperationException"/> at runtime. Only the *column* each
/// one maps to is UPPERCASE (see <c>Infrastructure.BannerConfiguration</c>'s explicit
/// <c>HasColumnName("CREATED_AT_UTC")</c> etc.) — the C# property name is the only part of those four that
/// stays PascalCase.
/// </para>
/// <para>
/// No <see cref="Siri.Persistence.Conventions.ISoftDelete"/> — Banners is not in database.md's soft-delete
/// table list (Course/Post/Discussion only), and an expired/retired banner an admin deletes has no "undo"
/// requirement the way paid/enrollment data does.
/// </para>
/// <para>
/// This is a scaffold pass (docs/DECISIONS.md D-17): every property and column mapping below is meant to be
/// final (so the later centralized migration pass can generate the real table from it), but
/// <see cref="Create"/> and everything in <c>Application/</c> are intentionally unimplemented — see
/// <see cref="Create"/>'s own doc comment.
/// </para>
/// </summary>
public sealed class BANNER : IAuditable
{
    /// <summary>EF Core materialization only — never used to build a usable instance from code.</summary>
    private BANNER()
    {
    }

    public Guid BANNER_ID { get; private set; }

    /// <summary>Which fixed UI slot this banner renders in (e.g. a homepage-hero carousel). A free-form
    /// string, not an enum: docs/DATABASE.md does not enumerate the valid placements, and inventing that
    /// list is a product decision for whichever later task builds the admin UI / public consumption
    /// endpoint, not this scaffold.</summary>
    public string PLACEMENT { get; private set; } = string.Empty;

    public string IMAGE_URL { get; private set; } = string.Empty;

    /// <summary>Optional mobile-optimized alternative to <see cref="IMAGE_URL"/> — <c>null</c> means reuse
    /// <see cref="IMAGE_URL"/> on small screens.</summary>
    public string? MOBILE_IMAGE_URL { get; private set; }

    /// <summary>Where the banner links to when clicked — <c>null</c> means the banner is not clickable.</summary>
    public string? LINK_URL { get; private set; }

    public string TITLE { get; private set; } = string.Empty;

    /// <summary>Display order among banners in the same <see cref="PLACEMENT"/>. Owned exclusively by a
    /// future Reorder operation — same "one mutation method owns this field" convention
    /// <c>Siri.Modules.Catalog.Domain.Category.SortOrder</c>'s own doc comment establishes, so a future
    /// Update operation should not also accept it.</summary>
    public int SORT_ORDER { get; private set; }

    /// <summary><c>null</c> = no scheduled start; visible immediately (subject to <see cref="IS_ACTIVE"/>).</summary>
    public DateTime? STARTS_AT_UTC { get; private set; }

    /// <summary><c>null</c> = no scheduled end; never auto-expires on its own.</summary>
    public DateTime? ENDS_AT_UTC { get; private set; }

    public bool IS_ACTIVE { get; private set; }

    // ---- IAuditable — stays PascalCase; see this class's own doc comment for why. ---------------------
    public DateTime CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    DateTime IAuditable.CreatedAtUtc
    {
        get => CreatedAtUtc;
        set => CreatedAtUtc = value;
    }

    Guid? IAuditable.CreatedBy
    {
        get => CreatedBy;
        set => CreatedBy = value;
    }

    DateTime? IAuditable.UpdatedAtUtc
    {
        get => UpdatedAtUtc;
        set => UpdatedAtUtc = value;
    }

    Guid? IAuditable.UpdatedBy
    {
        get => UpdatedBy;
        set => UpdatedBy = value;
    }

    public static BANNER Create(string placement, string imageUrl, string title, int sortOrder, string? mobileImageUrl = null, string? linkUrl = null, DateTime? startsAtUtc = null, DateTime? endsAtUtc = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(placement);
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new BANNER
        {
            BANNER_ID = UuidV7.NewId(),
            PLACEMENT = placement.Trim(),
            IMAGE_URL = imageUrl.Trim(),
            TITLE = title.Trim(),
            SORT_ORDER = sortOrder,
            MOBILE_IMAGE_URL = mobileImageUrl?.Trim(),
            LINK_URL = linkUrl?.Trim(),
            STARTS_AT_UTC = startsAtUtc,
            ENDS_AT_UTC = endsAtUtc,
            IS_ACTIVE = true,
        };
    }

    public void Update(string imageUrl, string? mobileImageUrl, string? linkUrl, string title, DateTime? startsAtUtc, DateTime? endsAtUtc, bool isActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imageUrl);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        IMAGE_URL = imageUrl.Trim();
        MOBILE_IMAGE_URL = mobileImageUrl?.Trim();
        LINK_URL = linkUrl?.Trim();
        TITLE = title.Trim();
        STARTS_AT_UTC = startsAtUtc;
        ENDS_AT_UTC = endsAtUtc;
        IS_ACTIVE = isActive;
    }

    public void SetSortOrder(int sortOrder) => SORT_ORDER = sortOrder;
}
