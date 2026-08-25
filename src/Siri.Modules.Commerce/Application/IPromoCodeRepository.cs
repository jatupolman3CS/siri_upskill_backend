using Siri.Modules.Commerce.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

public interface IPromoCodeRepository
{
    Task<PROMO_CODE?> GetByIdAsync(Guid promoCodeId, CancellationToken cancellationToken);

    /// <summary>The lookup the real checkout flow needs: a buyer types a code string, not an id —
    /// <see cref="PROMO_CODE.CODE"/> is unique, so this is as trivial/safe a fetch as
    /// <see cref="GetByIdAsync"/> (mirrors <c>IPaymentRepository.GetByProviderPaymentIntentIdAsync</c>'s own
    /// doc comment).</summary>
    Task<PROMO_CODE?> GetByCodeAsync(string code, CancellationToken cancellationToken);

    Task AddAsync(PROMO_CODE promoCode, CancellationToken cancellationToken);

    /// <summary>
    /// Atomically checks user redemption limits (locking against concurrency races) and increments
    /// <see cref="PROMO_CODE.REDEEMED_COUNT"/> — a raw
    /// <c>UPDATE ... SET REDEEMED_COUNT = REDEEMED_COUNT + 1 WHERE PROMO_CODE_ID = @id AND REDEEMED_COUNT &lt; MAX_REDEMPTIONS</c>,
    /// per <see cref="PROMO_CODE"/>'s own doc comment on why this cannot be a normal read-then-write
    /// (database.md: "นับโควตา ... ต้อง atomic ที่ระดับ SQL") — and, only if that update actually affected a
    /// row and the user has not exceeded <paramref name="maxPerUser"/>, inserts the matching <see cref="PROMO_REDEMPTION"/> via <see cref="PROMO_REDEMPTION.Create"/>.
    /// <para>
    /// Returns <c>false</c> if the code was already at <see cref="PROMO_CODE.MAX_REDEMPTIONS"/> (lost a race
    /// against other concurrent redemptions, or simply exhausted), or if the user has already reached <paramref name="maxPerUser"/>.
    /// </para>
    /// </summary>
    Task<bool> TryRedeemAsync(Guid promoCodeId, Guid orderId, Guid userId, int maxPerUser, IClock clock, CancellationToken cancellationToken);

    Task<int> GetUserRedemptionCountAsync(Guid promoCodeId, Guid userId, CancellationToken cancellationToken);

    /// <summary>
    /// Reverts a promo code redemption (when an order is cancelled or refunded) by decrementing REDEEMED_COUNT
    /// atomically and removing the PROMO_REDEMPTION record.
    /// </summary>
    Task RevertRedemptionAsync(Guid promoCodeId, Guid orderId, CancellationToken cancellationToken);

    /// <summary>Admin-facing paginated list — left as a stub; the exact filter (active only? expired
    /// included?) the admin promo-code screen needs is not specified by this scaffold task.</summary>
    Task<IReadOnlyList<PROMO_CODE>> ListAsync(int page, int pageSize, CancellationToken cancellationToken);

    Task<int> CountAsync(CancellationToken cancellationToken);
}
