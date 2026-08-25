using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Domain;

/// <summary>
/// A learner's shopping cart — aggregate root for its <see cref="CART_ITEMS"/>. One cart per user
/// (enforced by a unique index on <see cref="USER_ID"/> — see <c>CARTConfiguration</c>).
/// <para>
/// Naming: SCREAMING_SNAKE_CASE class/properties, DB table/columns — the same brand-new-module
/// exception documented in full on <see cref="ORDER"/>'s doc comment (docs/DECISIONS.md D-17).
/// </para>
/// <para>
/// No <see cref="Siri.Persistence.Conventions.IAuditable"/>: docs/DATABASE.md's column sketch for
/// <c>Carts</c> lists only <see cref="UPDATED_AT_UTC"/>, not the full Created/Updated audit quartet —
/// a cart is ephemeral pre-purchase scratch state, not something whose creation moment needs an audit
/// trail. <see cref="UPDATED_AT_UTC"/> is set directly by <see cref="AddItem"/>/<see cref="RemoveItem"/>
/// (passing <see cref="IClock"/> in), not by <c>AuditableEntityInterceptor</c>.
/// </para>
/// <para>
/// No <see cref="Siri.Persistence.Conventions.ISoftDelete"/> either, and unlike <see cref="ORDER"/>'s
/// items, <see cref="CART_ITEM"/> rows are genuinely hard-deleted when removed — a cart is not one of
/// the money/entitlement tables database.md's "ห้าม hard delete" rule protects (Orders, Payments,
/// Refunds, TaxInvoices; see this module's own no-cascade/no-hard-delete note in the scaffold task),
/// removing an item from a cart before checkout is just normal mutable state.
/// </para>
/// </summary>
public sealed class CART
{
    private readonly List<CART_ITEM> _cartItems = [];

    private CART()
    {
    }

    public Guid CART_ID { get; private set; }
    public Guid USER_ID { get; private set; }
    public DateTime UPDATED_AT_UTC { get; private set; }
    public IReadOnlyCollection<CART_ITEM> CART_ITEMS => _cartItems.AsReadOnly();

    public static CART Create(Guid userId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        return new CART
        {
            CART_ID = UuidV7.NewId(), USER_ID = userId, UPDATED_AT_UTC = clock.UtcNow,
        };
    }

    /// <summary>Appends a new item. Does not de-duplicate against an existing <see cref="CART_ITEM.REF_ID"/>
    /// of the same <see cref="CartItemType"/> — deciding whether "add" should merge into an existing line
    /// or always append a new one is real product/business logic left to whoever fills in
    /// <c>CartService.AddItemAsync</c>, not a domain-level invariant this scaffold pass takes a position on.</summary>
    public CART_ITEM AddItem(CartItemType itemType, Guid refId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var item = CART_ITEM.Create(CART_ID, itemType, refId, clock);
        _cartItems.Add(item);
        UPDATED_AT_UTC = clock.UtcNow;
        return item;
    }

    public void RemoveItem(Guid cartItemId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        var item = _cartItems.FirstOrDefault(i => i.CART_ITEM_ID == cartItemId);
        if (item is null)
        {
            throw new InvalidOperationException($"Cart {CART_ID} has no item {cartItemId}.");
        }

        _cartItems.Remove(item);
        UPDATED_AT_UTC = clock.UtcNow;
    }
}
