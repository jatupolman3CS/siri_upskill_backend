using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Cms.Domain;

/// <summary>
/// One node in the site navigation menu — self-referencing via <see cref="PARENT_ID"/>, the same shape
/// <see cref="Siri.Modules.Catalog.Domain.Category"/> uses for the course-category tree
/// (docs/DATABASE.md's "cms" section: "MenuItems(Id, ParentId, Label, Url, SortOrder, IsActive)";
/// docs/REQUIREMENTS.md AD-01: "จัด ... เมนู ... โดยไม่ต้อง deploy code").
/// <para>
/// <b>UPPERCASE naming exception</b> (docs/DECISIONS.md D-17) — see <see cref="BANNER"/>'s own doc comment
/// for the full reasoning; short version: this module's entity/property/table/column names are UPPERCASE
/// end to end except <see cref="IAuditable"/>'s four properties below, which must stay PascalCase because
/// <c>AuditableEntityInterceptor</c> looks them up by hardcoded C# member name.
/// </para>
/// <para>
/// No <c>Children</c>/<c>Parent</c> navigation, for the same reason
/// <see cref="Siri.Modules.Catalog.Domain.Category"/> has none: every read (public menu, admin tree)
/// assembles the tree from one flat query in memory, so a navigation property would only invite
/// lazy-loading foot-guns for no benefit. Not <see cref="Siri.Persistence.Conventions.ISoftDelete"/>
/// either, for the same reason <see cref="BANNER"/> isn't — this is admin-curated structure, not user
/// content someone needs "undo" for.
/// </para>
/// <para>
/// This is a scaffold pass (D-17): the shape below is meant to be final, but <see cref="Create"/> and
/// everything in <c>Application/</c> are intentionally unimplemented — see <see cref="Create"/>'s own doc
/// comment.
/// </para>
/// </summary>
public sealed class MENU_ITEM : IAuditable
{
    /// <summary>EF Core materialization only — never used to build a usable instance from code.</summary>
    private MENU_ITEM()
    {
    }

    public Guid MENU_ITEM_ID { get; private set; }

    /// <summary><c>null</c> = root-level menu item.</summary>
    public Guid? PARENT_ID { get; private set; }

    public string LABEL { get; private set; } = string.Empty;

    /// <summary>Internal route or external URL this menu item links to.</summary>
    public string URL { get; private set; } = string.Empty;

    /// <summary>Display order among siblings (same <see cref="PARENT_ID"/>). Owned exclusively by a future
    /// Reorder operation — same convention <see cref="BANNER.SORT_ORDER"/>/
    /// <see cref="Siri.Modules.Catalog.Domain.Category.SortOrder"/> already establish.</summary>
    public int SORT_ORDER { get; private set; }

    public bool IS_ACTIVE { get; private set; }

    // ---- IAuditable — stays PascalCase; see BANNER's own doc comment for why. ---------------------------
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

    public static MENU_ITEM Create(Guid? parentId, string label, string url, int sortOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);

        return new MENU_ITEM
        {
            MENU_ITEM_ID = UuidV7.NewId(),
            PARENT_ID = parentId,
            LABEL = label.Trim(),
            URL = url.Trim(),
            SORT_ORDER = sortOrder,
            IS_ACTIVE = true,
        };
    }

    public void Update(string label, string url, bool isActive)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(label);
        ArgumentException.ThrowIfNullOrWhiteSpace(url);
        LABEL = label.Trim();
        URL = url.Trim();
        IS_ACTIVE = isActive;
    }

    public void SetSortOrder(int sortOrder) => SORT_ORDER = sortOrder;
}
