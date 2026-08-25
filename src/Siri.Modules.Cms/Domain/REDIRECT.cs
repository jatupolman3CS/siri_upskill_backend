using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Cms.Domain;

/// <summary>
/// A URL redirect rule (e.g. an old course slug forwarding to its new one after a rename) —
/// docs/DATABASE.md's "cms" section: "Redirects(Id, FromPath UQ, ToPath, StatusCode)"; docs/TASKS.md's
/// P6-01 lists "redirect" alongside banner/menu/blog post as part of the same CMS API task.
/// <para>
/// <b>UPPERCASE naming exception</b> (docs/DECISIONS.md D-17) — see <see cref="BANNER"/>'s own doc comment
/// for the full reasoning; short version: this module's entity/property/table/column names are UPPERCASE
/// end to end except <see cref="IAuditable"/>'s four properties below, which must stay PascalCase because
/// <c>AuditableEntityInterceptor</c> looks them up by hardcoded C# member name.
/// </para>
/// <para>
/// No <see cref="Siri.Persistence.Conventions.ISoftDelete"/> — same "admin-curated structure, not user
/// content" reasoning <see cref="BANNER"/>/<see cref="MENU_ITEM"/> give; a stale redirect rule an admin
/// removes has no "undo" requirement.
/// </para>
/// <para>
/// This is a scaffold pass (D-17): the shape below is meant to be final, but <see cref="Create"/>,
/// everything in <c>Application/</c>, and the actual redirect-resolving middleware that would look rows up
/// by <see cref="FROM_PATH"/> (out of scope for this task entirely — nothing wires these rows into the
/// request pipeline yet) are intentionally unimplemented/unbuilt.
/// </para>
/// </summary>
public sealed class REDIRECT : IAuditable
{
    /// <summary>EF Core materialization only — never used to build a usable instance from code.</summary>
    private REDIRECT()
    {
    }

    public Guid REDIRECT_ID { get; private set; }

    /// <summary>The incoming request path this rule matches, e.g. <c>/old-course-slug</c>. Unique — see
    /// <c>Infrastructure.RedirectConfiguration</c>.</summary>
    public string FROM_PATH { get; private set; } = string.Empty;

    /// <summary>Where <see cref="FROM_PATH"/> forwards to — either an internal path or a full external URL.</summary>
    public string TO_PATH { get; private set; } = string.Empty;

    /// <summary>HTTP redirect status, e.g. 301 (permanent) or 302 (temporary).</summary>
    public int STATUS_CODE { get; private set; }

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

    public static REDIRECT Create(string fromPath, string toPath, int statusCode)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fromPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(toPath);

        return new REDIRECT
        {
            REDIRECT_ID = UuidV7.NewId(),
            FROM_PATH = fromPath.Trim(),
            TO_PATH = toPath.Trim(),
            STATUS_CODE = statusCode,
        };
    }
}
