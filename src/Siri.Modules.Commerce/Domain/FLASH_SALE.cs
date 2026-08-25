using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Domain;

/// <summary>
/// A time-boxed discount event across several courses — aggregate root for its
/// <see cref="FLASH_SALE_ITEMS"/>. A public catalog-style resource, same status as <see cref="BUNDLE"/>.
/// <para>
/// Naming: SCREAMING_SNAKE_CASE class/properties, DB table/columns — the same brand-new-module
/// exception documented in full on <see cref="ORDER"/>'s doc comment (docs/DECISIONS.md D-17).
/// </para>
/// <para>
/// No <see cref="Siri.Persistence.Conventions.IAuditable"/>/<see cref="Siri.Persistence.Conventions.ISoftDelete"/>:
/// docs/DATABASE.md's column sketch lists no Created/UpdatedAtUtc — <see cref="IS_ACTIVE"/> is the
/// publish/unpublish switch, same reasoning as <see cref="BUNDLE.IS_ACTIVE"/>.
/// </para>
/// </summary>
public sealed class FLASH_SALE
{
    private readonly List<FLASH_SALE_ITEM> _flashSaleItems = [];

    private FLASH_SALE()
    {
    }

    public Guid FLASH_SALE_ID { get; private set; }
    public string TITLE { get; private set; } = string.Empty;
    public DateTime STARTS_AT_UTC { get; private set; }
    public DateTime ENDS_AT_UTC { get; private set; }
    public bool IS_ACTIVE { get; private set; }
    public IReadOnlyCollection<FLASH_SALE_ITEM> FLASH_SALE_ITEMS => _flashSaleItems.AsReadOnly();

    public static FLASH_SALE Create(string title, DateTime startsAtUtc, DateTime endsAtUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        if (endsAtUtc <= startsAtUtc) throw new ArgumentOutOfRangeException(nameof(endsAtUtc), endsAtUtc, "endsAtUtc must be after startsAtUtc.");

        return new FLASH_SALE
        {
            FLASH_SALE_ID = UuidV7.NewId(), TITLE = title, STARTS_AT_UTC = startsAtUtc, ENDS_AT_UTC = endsAtUtc, IS_ACTIVE = true,
        };
    }

    public void Reschedule(DateTime startsAtUtc, DateTime endsAtUtc)
    {
        if (endsAtUtc <= startsAtUtc) throw new ArgumentOutOfRangeException(nameof(endsAtUtc), endsAtUtc, "endsAtUtc must be after startsAtUtc.");

        STARTS_AT_UTC = startsAtUtc;
        ENDS_AT_UTC = endsAtUtc;
    }

    public void Activate() => IS_ACTIVE = true;

    public void Deactivate() => IS_ACTIVE = false;

    /// <summary>Adds a course at a sale price. No duplicate check against an existing
    /// <see cref="FLASH_SALE_ITEM.COURSE_ID"/> in memory — <c>FLASH_SALE_ITEMConfiguration</c>'s unique
    /// index on (<see cref="FLASH_SALE_ID"/>, CourseId) enforces "a course appears at most once per sale"
    /// at the database level (this entity, unlike <see cref="BUNDLE_ITEM"/>, keeps its own surrogate id,
    /// so a real unique index is used here rather than a composite primary key).</summary>
    public FLASH_SALE_ITEM AddItem(Guid courseId, decimal salePrice)
    {
        var item = FLASH_SALE_ITEM.Create(FLASH_SALE_ID, courseId, salePrice);
        _flashSaleItems.Add(item);
        return item;
    }

    public void RemoveItem(Guid flashSaleItemId)
    {
        var item = _flashSaleItems.FirstOrDefault(i => i.FLASH_SALE_ITEM_ID == flashSaleItemId);
        if (item is null)
        {
            throw new InvalidOperationException($"Flash sale {FLASH_SALE_ID} has no item {flashSaleItemId}.");
        }

        _flashSaleItems.Remove(item);
    }
}
