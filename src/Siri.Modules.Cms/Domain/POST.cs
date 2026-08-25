using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Cms.Domain;

/// <summary>
/// A blog/article post — the "บทความ SEO" table from the admin CMS mockup (docs/DECISIONS.md D-17;
/// docs/DATABASE.md's "cms" section: "Posts(Id, Slug UQ, Title, Excerpt, ContentHtml, CoverImageUrl,
/// AuthorUserId, Status, PublishedAtUtc, SeoTitle, SeoDescription, IsDeleted)"; docs/REQUIREMENTS.md AD-01:
/// "เขียน blog/article ที่ทำ SEO ได้ โดยไม่ต้อง deploy code").
/// <para>
/// <b>UPPERCASE naming exception</b> (docs/DECISIONS.md D-17) — see <see cref="BANNER"/>'s own doc comment
/// for the full reasoning; short version: this module's entity/property/table/column names are UPPERCASE
/// end to end except <see cref="IAuditable"/>/<see cref="ISoftDelete"/>'s properties below, which must stay
/// PascalCase because <c>AuditableEntityInterceptor</c> looks them up by hardcoded C# member name
/// (<c>nameof(IAuditable.CreatedAtUtc)</c> etc.) — renaming them would make every <c>SaveChangesAsync</c>
/// throw <see cref="InvalidOperationException"/> at runtime.
/// </para>
/// <para>
/// <b><see cref="Siri.Persistence.Conventions.ISoftDelete"/></b> — Posts IS in docs/DATABASE.md's explicit
/// soft-delete table list ("Course, Post, Discussion"), unlike the other three Cms entities in this module
/// (Banners/MenuItems/Redirects), which are admin-curated structure with no "undo" requirement. A published
/// article can have external backlinks, search-engine indexing, and reader bookmarks pointing at its slug —
/// hard-deleting it out from under those is a worse failure mode than a stale banner or menu item
/// disappearing, so this follows the same convention <see cref="Siri.Modules.Catalog.Domain.Course"/>
/// already established for the same reason.
/// </para>
/// <para>
/// <b><see cref="CONTENT_HTML"/> is untrusted input</b> (.claude/rules/security.md: "HTML จาก CMS ต้อง
/// sanitize ที่ server ด้วย allowlist ก่อนเก็บ และก่อนแสดง") — see that property's own inline
/// <c>TODO</c>, plus the matching ones on <c>Infrastructure.PostConfiguration</c> and
/// <c>Application.PostService</c>. This scaffold pass does not implement the sanitizer (a later task);
/// nothing here should be read as having already made this field safe to store or render.
/// </para>
/// <para>
/// This is a scaffold pass (D-17): every property and column mapping below is meant to be final, but
/// <see cref="Create"/> and everything in <c>Application/</c> (including the sanitizer) are intentionally
/// unimplemented — see <see cref="Create"/>'s own doc comment.
/// </para>
/// </summary>
public sealed class POST : IAuditable, ISoftDelete
{
    /// <summary>EF Core materialization only — never used to build a usable instance from code.</summary>
    private POST()
    {
    }

    public Guid POST_ID { get; private set; }

    public string SLUG { get; private set; } = string.Empty;

    public string TITLE { get; private set; } = string.Empty;

    public string EXCERPT { get; private set; } = string.Empty;

    // TODO(later task): sanitize server-side with an allowlist, both on save (Application.PostService's
    // Create/Update) and on render (whatever public read endpoint eventually serves this) — see
    // .claude/rules/security.md: "HTML จาก CMS ต้อง sanitize ที่ server ด้วย allowlist ก่อนเก็บ และก่อน
    // แสดง". Not implemented in this scaffold pass — this field is NOT safe to store or render as-is yet;
    // do not wire it as if it already were.
    public string CONTENT_HTML { get; private set; } = string.Empty;

    public string? COVER_IMAGE_URL { get; private set; }

    /// <summary>No FK — cross-module/schema reference to <c>identity.Users.Id</c>, the same "no FK across
    /// module schemas" convention <see cref="Siri.Modules.Catalog.Domain.Course.TrailerMediaAssetId"/>'s
    /// own doc comment explains in full.</summary>
    public Guid AUTHOR_USER_ID { get; private set; }

    public PostStatus STATUS { get; private set; }

    public DateTime? PUBLISHED_AT_UTC { get; private set; }

    public string? SEO_TITLE { get; private set; }

    public string? SEO_DESCRIPTION { get; private set; }

    // ---- ISoftDelete — stays PascalCase; see this class's own doc comment for why. ----------------------
    public bool IsDeleted { get; private set; }

    public DateTime? DeletedAtUtc { get; private set; }

    bool ISoftDelete.IsDeleted
    {
        get => IsDeleted;
        set => IsDeleted = value;
    }

    DateTime? ISoftDelete.DeletedAtUtc
    {
        get => DeletedAtUtc;
        set => DeletedAtUtc = value;
    }

    // ---- IAuditable — stays PascalCase; see this class's own doc comment for why. -----------------------
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

    public static POST Create(string slug, string title, string excerpt, string contentHtml, Guid authorUserId, string? coverImageUrl = null, string? seoTitle = null, string? seoDescription = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (authorUserId == Guid.Empty) throw new ArgumentException("Author user ID cannot be empty.", nameof(authorUserId));

        return new POST
        {
            POST_ID = UuidV7.NewId(),
            SLUG = slug.Trim().ToLowerInvariant(),
            TITLE = title.Trim(),
            EXCERPT = excerpt ?? string.Empty,
            CONTENT_HTML = contentHtml ?? string.Empty,
            AUTHOR_USER_ID = authorUserId,
            COVER_IMAGE_URL = coverImageUrl?.Trim(),
            STATUS = PostStatus.Draft,
            SEO_TITLE = seoTitle?.Trim(),
            SEO_DESCRIPTION = seoDescription?.Trim(),
            IsDeleted = false,
        };
    }

    public void Update(string title, string excerpt, string contentHtml, string? coverImageUrl, string? seoTitle, string? seoDescription)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        TITLE = title.Trim();
        EXCERPT = excerpt ?? string.Empty;
        CONTENT_HTML = contentHtml ?? string.Empty;
        COVER_IMAGE_URL = coverImageUrl?.Trim();
        SEO_TITLE = seoTitle?.Trim();
        SEO_DESCRIPTION = seoDescription?.Trim();
    }

    public void ChangeStatus(PostStatus newStatus, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        STATUS = newStatus;
        if (newStatus == PostStatus.Published && PUBLISHED_AT_UTC is null)
        {
            PUBLISHED_AT_UTC = clock.UtcNow;
        }
    }

    public void SoftDelete(DateTime nowUtc)
    {
        IsDeleted = true;
        DeletedAtUtc = nowUtc;
    }
}
