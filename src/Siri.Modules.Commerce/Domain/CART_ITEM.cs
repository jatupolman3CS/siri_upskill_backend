using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Domain;

/// <summary>
/// One line in a <see cref="CART"/> — pure composition child, constructed only through
/// <see cref="CART.AddItem"/> (shared UPPERCASE naming exception — see <see cref="ORDER"/>'s doc comment).
/// </summary>
public sealed class CART_ITEM
{
    private CART_ITEM()
    {
    }

    public Guid CART_ITEM_ID { get; private set; }
    public Guid CART_ID { get; private set; }
    public CartItemType ITEM_TYPE { get; private set; }

    /// <summary>Conceptual FK to <c>catalog.Courses.Id</c> (when <see cref="ITEM_TYPE"/> is
    /// <see cref="CartItemType.Course"/>) or this module's own <see cref="BUNDLE"/>.<see cref="BUNDLE.BUNDLE_ID"/>
    /// (when <see cref="CartItemType.Bundle"/>) — polymorphic by <see cref="ITEM_TYPE"/>, so it can never
    /// carry a single real FK constraint even for the same-module Bundle case (a single column cannot be
    /// two different FKs at once). Existence is an application-layer concern for whoever implements
    /// <c>CartService.AddItemAsync</c>.</summary>
    public Guid REF_ID { get; private set; }

    public DateTime ADDED_AT_UTC { get; private set; }

    internal static CART_ITEM Create(Guid cartId, CartItemType itemType, Guid refId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        return new CART_ITEM
        {
            CART_ITEM_ID = UuidV7.NewId(), CART_ID = cartId, ITEM_TYPE = itemType, REF_ID = refId,
            ADDED_AT_UTC = clock.UtcNow,
        };
    }
}
