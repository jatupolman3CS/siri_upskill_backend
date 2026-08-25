using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Domain;

/// <summary>
/// A fixed-price grouping of courses sold together — aggregate root for its <see cref="BUNDLE_ITEMS"/>.
/// A public catalog-style resource (like <see cref="Siri.Modules.Catalog.Domain.Course"/>): browsable
/// with no per-user ownership, unlike <see cref="ORDER"/>/<see cref="CART"/>.
/// <para>
/// Naming: SCREAMING_SNAKE_CASE class/properties, DB table/columns — the same brand-new-module
/// exception documented in full on <see cref="ORDER"/>'s doc comment (docs/DECISIONS.md D-17).
/// </para>
/// <para>
/// No <see cref="Siri.Persistence.Conventions.IAuditable"/>/<see cref="Siri.Persistence.Conventions.ISoftDelete"/>:
/// docs/DATABASE.md's column sketch lists no Created/UpdatedAtUtc — <see cref="IS_ACTIVE"/> is the
/// publish/unpublish switch (mirrors <see cref="PROMO_CODE.IS_ACTIVE"/>), not a soft-delete flag; a
/// bundle is priced catalog metadata, not one of the money/entitlement tables the no-hard-delete rule
/// protects.
/// </para>
/// </summary>
public sealed class BUNDLE
{
    private readonly List<BUNDLE_ITEM> _bundleItems = [];

    private BUNDLE()
    {
    }

    public Guid BUNDLE_ID { get; private set; }
    public string SLUG { get; private set; } = string.Empty;
    public string TITLE { get; private set; } = string.Empty;
    public string? DESCRIPTION { get; private set; }
    public decimal PRICE { get; private set; }
    public bool IS_ACTIVE { get; private set; }
    public DateTime? STARTS_AT_UTC { get; private set; }
    public DateTime? ENDS_AT_UTC { get; private set; }
    public IReadOnlyCollection<BUNDLE_ITEM> BUNDLE_ITEMS => _bundleItems.AsReadOnly();

    public static BUNDLE Create(string slug, string title, string? description, decimal price)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(slug);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (price < 0) throw new ArgumentOutOfRangeException(nameof(price), price, "price cannot be negative.");

        return new BUNDLE
        {
            BUNDLE_ID = UuidV7.NewId(), SLUG = slug, TITLE = title, DESCRIPTION = description,
            PRICE = price, IS_ACTIVE = true,
        };
    }

    public void UpdateBasicInfo(string title, string? description)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        TITLE = title;
        DESCRIPTION = description;
    }

    public void SetPrice(decimal price)
    {
        if (price < 0) throw new ArgumentOutOfRangeException(nameof(price), price, "price cannot be negative.");
        PRICE = price;
    }

    public void SetSchedule(DateTime? startsAtUtc, DateTime? endsAtUtc)
    {
        if (startsAtUtc is not null && endsAtUtc is not null && endsAtUtc <= startsAtUtc)
        {
            throw new ArgumentOutOfRangeException(nameof(endsAtUtc), endsAtUtc, "endsAtUtc must be after startsAtUtc.");
        }

        STARTS_AT_UTC = startsAtUtc;
        ENDS_AT_UTC = endsAtUtc;
    }

    public void Activate() => IS_ACTIVE = true;

    public void Deactivate() => IS_ACTIVE = false;

    /// <summary>Adds a course to this bundle. No duplicate check against an existing
    /// <see cref="BUNDLE_ITEM.COURSE_ID"/> — <see cref="BUNDLE_ITEMConfiguration"/>'s composite primary
    /// key on (<see cref="BUNDLE_ID"/>, CourseId) is what actually enforces "a course appears at most
    /// once per bundle", at the database level, the same place every other uniqueness rule in this
    /// scaffold pass is enforced.</summary>
    public BUNDLE_ITEM AddItem(Guid courseId)
    {
        var item = BUNDLE_ITEM.Create(BUNDLE_ID, courseId);
        _bundleItems.Add(item);
        return item;
    }

    public void RemoveItem(Guid courseId)
    {
        var item = _bundleItems.FirstOrDefault(i => i.COURSE_ID == courseId);
        if (item is null)
        {
            throw new InvalidOperationException($"Bundle {BUNDLE_ID} has no item for course {courseId}.");
        }

        _bundleItems.Remove(item);
    }
}
