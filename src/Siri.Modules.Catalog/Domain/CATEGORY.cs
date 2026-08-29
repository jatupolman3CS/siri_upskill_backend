using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Domain;

/// <summary>
/// A node in the course-category tree (docs/DATABASE.md "catalog" section: "Categories(Id PK,
/// ParentId FK->self, Slug UQ, NameTh, NameEn, IconKey, SortOrder, IsActive)"). Self-referencing via
/// <see cref="ParentId"/> — <c>null</c> means a root category.
/// <para>
/// Protects its own invariants the same way <c>Siri.Modules.Identity.Domain.User</c> does: every
/// property has a private setter and can only change through the methods below. No
/// <c>Children</c>/<c>Parent</c> navigation collection — every read (public tree, admin tree) works
/// from one flat query and assembles the tree in memory (see
/// <c>Infrastructure.CategoryTreeAssembler</c>), so a navigation property would only invite
/// lazy-loading foot-guns for no benefit.
/// </para>
/// <para>
/// Not <see cref="Siri.Persistence.Conventions.ISoftDelete"/> — CATEGORY is not in DATABASE.md's
/// documented soft-delete table list (COURSE/Post/Discussion only). A genuine SQL delete is correct
/// here: this is admin-curated taxonomy, not user content someone needs "undo" for. The Delete
/// handler rejects deleting a category that still has children; the self-referencing FK's
/// <c>OnDelete(Restrict)</c> (see <c>Infrastructure.CategoryConfiguration</c>) is the DB-level
/// backstop for the race where a child is inserted between that check and the delete's own
/// <c>SaveChangesAsync</c>.
/// </para>
/// </summary>
public sealed class CATEGORY : IAuditable
{
    /// <summary>EF Core materialization only — never used to build a usable instance from code.</summary>
    private CATEGORY()
    {
    }

    public Guid Id { get; private set; }

    /// <summary><c>null</c> = root category.</summary>
    public Guid? ParentId { get; private set; }

    public string Slug { get; private set; } = string.Empty;

    public string NameTh { get; private set; } = string.Empty;

    public string NameEn { get; private set; } = string.Empty;

    /// <summary>Icon-library key for the UI (e.g. a Phosphor icon name) — optional, not a required asset.</summary>
    public string? IconKey { get; private set; }

    /// <summary>Display order among siblings (same <see cref="ParentId"/>). Owned exclusively by
    /// <see cref="Reorder"/> — other mutation methods never touch it, so two different admin actions
    /// can never race to set it with different intent.</summary>
    public int SortOrder { get; private set; }

    /// <summary>Whether this category (and, per <c>CategoryTreeAssembler</c>'s pruning rule, everything
    /// under it) is visible on the public tree.</summary>
    public bool IsActive { get; private set; }

    // ---- IAuditable ---------------------------------------------------------------------------
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

    /// <summary>Creates a new category. <paramref name="sortOrder"/> is the caller's responsibility
    /// (the Create handler computes "append to end of siblings" — the entity has no way to know its
    /// siblings). Starts <see cref="IsActive"/>.</summary>
    public static CATEGORY Create(string slug, string nameTh, string nameEn, string? iconKey, Guid? parentId, int sortOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(nameTh);
        ArgumentException.ThrowIfNullOrWhiteSpace(nameEn);

        return new CATEGORY
        {
            Id = UuidV7.NewId(),
            Slug = slug,
            NameTh = nameTh,
            NameEn = nameEn,
            IconKey = iconKey,
            ParentId = parentId,
            SortOrder = sortOrder,
            IsActive = true,
        };
    }

    public void Rename(string nameTh, string nameEn)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nameTh);
        ArgumentException.ThrowIfNullOrWhiteSpace(nameEn);

        NameTh = nameTh;
        NameEn = nameEn;
    }

    public void ChangeSlug(string slug)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);

        Slug = slug;
    }

    public void SetIcon(string? iconKey)
    {
        IconKey = iconKey;
    }

    public void Reorder(int sortOrder)
    {
        SortOrder = sortOrder;
    }

    public void Activate()
    {
        IsActive = true;
    }

    public void Deactivate()
    {
        IsActive = false;
    }

    /// <summary>
    /// Re-parents this category. Only rejects the trivial "move under myself" case — detecting the
    /// general cycle case (moving under one of my own descendants) needs the whole tree, which this
    /// entity cannot query (backend.md: domain stays EF/HTTP-ignorant). That check is the Update
    /// handler's job (it loads the flat category list once and walks ancestors of
    /// <paramref name="newParentId"/>). This guard stays anyway — cheap, and protects any future
    /// caller of <see cref="MoveTo"/> that isn't routed through that handler.
    /// </summary>
    public void MoveTo(Guid? newParentId)
    {
        if (newParentId == Id)
        {
            throw new InvalidOperationException("A category cannot be moved under itself.");
        }

        ParentId = newParentId;
    }
}
