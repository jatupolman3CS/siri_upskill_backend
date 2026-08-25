using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Domain;

/// <summary>
/// One line item of an <see cref="ORDER"/> — pure composition child, constructed only through
/// <see cref="ORDER.AddItem"/> (see this module's <c>ORDER</c> doc comment for the shared UPPERCASE
/// naming exception, which applies here too).
/// </summary>
public sealed class ORDER_ITEM
{
    private ORDER_ITEM()
    {
    }

    public Guid ORDER_ITEM_ID { get; private set; }
    public Guid ORDER_ID { get; private set; }

    /// <summary>Conceptual FK to <c>catalog.Courses.Id</c> — cross-module/schema, NEVER a real DB FK
    /// constraint (same reasoning as <c>Course.TrailerMediaAssetId</c> in Siri.Modules.Catalog: a
    /// database-level FK spanning two modules' schemas is exactly the physical coupling
    /// docs/ARCHITECTURE.md §1 keeps modules as separate projects to avoid). Nullable because a future
    /// bundle-purchase line item may not resolve to a single course row.</summary>
    public Guid? COURSE_ID { get; private set; }

    public string TITLE_SNAPSHOT { get; private set; } = string.Empty;
    public decimal UNIT_PRICE { get; private set; }
    public decimal LINE_TOTAL { get; private set; }

    internal static ORDER_ITEM Create(Guid orderId, Guid? courseId, string titleSnapshot, decimal unitPrice, decimal lineTotal)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(titleSnapshot);

        return new ORDER_ITEM
        {
            ORDER_ITEM_ID = UuidV7.NewId(), ORDER_ID = orderId, COURSE_ID = courseId,
            TITLE_SNAPSHOT = titleSnapshot, UNIT_PRICE = unitPrice, LINE_TOTAL = lineTotal,
        };
    }
}
