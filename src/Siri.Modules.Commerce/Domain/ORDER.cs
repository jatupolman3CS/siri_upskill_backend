using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Domain;

/// <summary>
/// A customer order — aggregate root for its <see cref="ORDER_ITEMS"/> line items.
/// <para>
/// Naming: this class and its properties are SCREAMING_SNAKE_CASE, not the PascalCase every other
/// entity in this codebase uses (contrast <see cref="Siri.Modules.Catalog.Domain.Course"/>) — an
/// explicit, project-owner-approved exception (docs/DECISIONS.md D-17) scoped to brand-new modules'
/// entity classes/properties and DB table/column names only. Everything else about this class
/// (private setters, static <c>Create</c> factory, behavior methods guarding invariants) follows
/// <c>Course</c>'s shape exactly.
/// </para>
/// <para>
/// <b>IAuditable exception</b>: <see cref="CreatedAtUtc"/>/<see cref="CreatedBy"/>/<see cref="UpdatedAtUtc"/>/
/// <see cref="UpdatedBy"/> keep normal PascalCase C# names even here. <c>AuditableEntityInterceptor</c>
/// writes stamps via <c>entry.Property(nameof(IAuditable.CreatedAtUtc)).CurrentValue = ...</c> — a
/// hardcoded string lookup by EF's logical property name, which for a CLR-backed property always
/// equals the CLR member name (HasColumnName only renames the column, not this). Rename the C#
/// property and every SaveChangesAsync on this entity throws InvalidOperationException at runtime,
/// not at compile time. The DB column is still SCREAMING_SNAKE_CASE via explicit HasColumnName — see
/// <c>ORDERConfiguration</c>. Do not "fix" this without also changing AuditableEntityInterceptor.
/// </para>
/// <para>
/// No <see cref="ISoftDelete"/>: per docs/DATABASE.md, soft-delete is Course/Post/Discussion only —
/// money tables use a status enum instead ("ห้าม Hard delete ข้อมูลการเงิน ... ใช้สถานะ"). Cancel/refund
/// are status transitions (see <see cref="MarkAwaitingPayment"/>/<see cref="MarkPaid"/>), never a delete.
/// </para>
/// </summary>
public sealed class ORDER : IAuditable
{
    private const string OnlySupportedCurrency = "THB";

    private readonly List<ORDER_ITEM> _orderItems = [];

    private ORDER()
    {
    }

    public Guid ORDER_ID { get; private set; }
    public string ORDER_NO { get; private set; } = string.Empty;
    public Guid USER_ID { get; private set; }
    public decimal SUBTOTAL_AMOUNT { get; private set; }
    public decimal DISCOUNT_AMOUNT { get; private set; }
    public decimal TAX_AMOUNT { get; private set; }
    public decimal TOTAL_AMOUNT { get; private set; }
    public string CURRENCY { get; private set; } = OnlySupportedCurrency;
    public OrderStatus STATUS { get; private set; }
    public Guid? PROMO_CODE_ID { get; private set; }
    public DateTime? PAID_AT_UTC { get; private set; }
    public byte[] ROW_VERSION { get; private set; } = [];
    public IReadOnlyCollection<ORDER_ITEM> ORDER_ITEMS => _orderItems.AsReadOnly();

    public DateTime CreatedAtUtc { get; private set; }
    public Guid? CreatedBy { get; private set; }
    public DateTime? UpdatedAtUtc { get; private set; }
    public Guid? UpdatedBy { get; private set; }

    DateTime IAuditable.CreatedAtUtc { get => CreatedAtUtc; set => CreatedAtUtc = value; }
    Guid? IAuditable.CreatedBy { get => CreatedBy; set => CreatedBy = value; }
    DateTime? IAuditable.UpdatedAtUtc { get => UpdatedAtUtc; set => UpdatedAtUtc = value; }
    Guid? IAuditable.UpdatedBy { get => UpdatedBy; set => UpdatedBy = value; }

    public static ORDER Create(string orderNo, Guid userId, decimal subtotalAmount, decimal discountAmount, decimal taxAmount, decimal totalAmount, Guid? promoCodeId = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(orderNo);
        if (subtotalAmount < 0) throw new ArgumentOutOfRangeException(nameof(subtotalAmount), subtotalAmount, "subtotalAmount cannot be negative.");
        if (totalAmount < 0) throw new ArgumentOutOfRangeException(nameof(totalAmount), totalAmount, "totalAmount cannot be negative.");

        return new ORDER
        {
            ORDER_ID = UuidV7.NewId(), ORDER_NO = orderNo, USER_ID = userId,
            SUBTOTAL_AMOUNT = subtotalAmount, DISCOUNT_AMOUNT = discountAmount, TAX_AMOUNT = taxAmount,
            TOTAL_AMOUNT = totalAmount, CURRENCY = OnlySupportedCurrency, STATUS = OrderStatus.Pending,
            PROMO_CODE_ID = promoCodeId,
        };
    }

    public ORDER_ITEM AddItem(Guid? courseId, string titleSnapshot, decimal unitPrice, decimal lineTotal)
    {
        var item = ORDER_ITEM.Create(ORDER_ID, courseId, titleSnapshot, unitPrice, lineTotal);
        _orderItems.Add(item);
        return item;
    }

    public void MarkAwaitingPayment()
    {
        if (STATUS != OrderStatus.Pending) throw new InvalidOperationException($"Cannot mark an order in {STATUS} status as awaiting payment.");
        STATUS = OrderStatus.AwaitingPayment;
    }

    public void MarkPaid(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (STATUS is not (OrderStatus.AwaitingPayment or OrderStatus.Cancelled or OrderStatus.Pending))
        {
            throw new InvalidOperationException($"Cannot mark an order in {STATUS} status as paid.");
        }

        STATUS = OrderStatus.Paid;
        PAID_AT_UTC = clock.UtcNow;
    }

    public void MarkCancelled()
    {
        if (STATUS is OrderStatus.Paid or OrderStatus.Refunded)
        {
            throw new InvalidOperationException($"Cannot cancel an order in {STATUS} status.");
        }

        STATUS = OrderStatus.Cancelled;
    }
}
