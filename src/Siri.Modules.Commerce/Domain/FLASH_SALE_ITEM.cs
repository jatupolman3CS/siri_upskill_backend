using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Domain;

/// <summary>
/// One course's sale price inside a <see cref="FLASH_SALE"/> — pure composition child, constructed only
/// through <see cref="FLASH_SALE.AddItem"/>.
/// </summary>
public sealed class FLASH_SALE_ITEM
{
    private FLASH_SALE_ITEM()
    {
    }

    public Guid FLASH_SALE_ITEM_ID { get; private set; }
    public Guid FLASH_SALE_ID { get; private set; }

    /// <summary>Conceptual FK to <c>catalog.Courses.Id</c> — cross-module/schema, never a real DB FK
    /// constraint (same reasoning as <see cref="ORDER_ITEM.COURSE_ID"/>).</summary>
    public Guid COURSE_ID { get; private set; }

    public decimal SALE_PRICE { get; private set; }

    internal static FLASH_SALE_ITEM Create(Guid flashSaleId, Guid courseId, decimal salePrice)
    {
        if (salePrice < 0) throw new ArgumentOutOfRangeException(nameof(salePrice), salePrice, "salePrice cannot be negative.");

        return new FLASH_SALE_ITEM
        {
            FLASH_SALE_ITEM_ID = UuidV7.NewId(), FLASH_SALE_ID = flashSaleId, COURSE_ID = courseId, SALE_PRICE = salePrice,
        };
    }
}
