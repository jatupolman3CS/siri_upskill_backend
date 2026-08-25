using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Domain;

/// <summary>
/// One redemption of a <see cref="PROMO_CODE"/> against an <see cref="ORDER"/> — a composition child of
/// <see cref="PROMO_CODE"/> (same "true composition child" status as <see cref="ORDER_ITEM"/>/
/// <see cref="CART_ITEM"/>/<see cref="BUNDLE_ITEM"/>/<see cref="FLASH_SALE_ITEM"/>, per this scaffold
/// task's own instructions), constructed only through <see cref="PROMO_CODE"/>'s owning service —
/// unlike those other child entities this one has two FKs (<see cref="PROMO_CODE_ID"/> and
/// <see cref="ORDER_ID"/>); see <c>PROMO_REDEMPTIONConfiguration</c>'s doc comment for why only the
/// first cascades.
/// <para>
/// Not <c>internal</c>-constructed through a parent aggregate method (contrast <see cref="ORDER.AddItem"/>):
/// creating a redemption row is inseparable from the atomic redemption-count update described on
/// <see cref="PROMO_CODE"/>'s doc comment, which is a repository/service-layer operation, not something
/// the <see cref="PROMO_CODE"/> aggregate can do to itself in memory. <see cref="Create"/> is <c>public</c>
/// accordingly, to be called by whoever implements that stub.
/// </para>
/// </summary>
public sealed class PROMO_REDEMPTION
{
    private PROMO_REDEMPTION()
    {
    }

    public Guid PROMO_REDEMPTION_ID { get; private set; }
    public Guid PROMO_CODE_ID { get; private set; }
    public Guid ORDER_ID { get; private set; }

    /// <summary>Conceptual FK to <c>identity.Users.Id</c> — cross-module/schema, never a real DB FK
    /// constraint (same reasoning as <see cref="ORDER.USER_ID"/>).</summary>
    public Guid USER_ID { get; private set; }

    public DateTime REDEEMED_AT_UTC { get; private set; }

    public static PROMO_REDEMPTION Create(Guid promoCodeId, Guid orderId, Guid userId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        return new PROMO_REDEMPTION
        {
            PROMO_REDEMPTION_ID = UuidV7.NewId(), PROMO_CODE_ID = promoCodeId, ORDER_ID = orderId,
            USER_ID = userId, REDEEMED_AT_UTC = clock.UtcNow,
        };
    }
}
