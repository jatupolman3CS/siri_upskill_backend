using Microsoft.EntityFrameworkCore;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Infrastructure;

public sealed class PromoCodeRepository(AppDbContext dbContext) : IPromoCodeRepository
{
    public Task<PROMO_CODE?> GetByIdAsync(Guid promoCodeId, CancellationToken cancellationToken) =>
        dbContext.PromoCodes().FirstOrDefaultAsync(p => p.PROMO_CODE_ID == promoCodeId, cancellationToken);

    public Task<PROMO_CODE?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
        dbContext.PromoCodes().FirstOrDefaultAsync(p => p.CODE == code.Trim().ToUpperInvariant(), cancellationToken);

    public async Task AddAsync(PROMO_CODE promoCode, CancellationToken cancellationToken)
    {
        dbContext.PromoCodes().Add(promoCode);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> TryRedeemAsync(Guid promoCodeId, Guid orderId, Guid userId, int maxPerUser, IClock clock, CancellationToken cancellationToken)
    {
        // 1. Atomic check with key-range locking on (PROMO_CODE_ID, USER_ID) to prevent concurrent double-spend TOCTOU race
        var userRedemptions = await dbContext.PromoRedemptions()
            .FromSqlInterpolated($"SELECT * FROM COMMERCE.PROMO_REDEMPTIONS WITH (UPDLOCK, HOLDLOCK) WHERE PROMO_CODE_ID = {promoCodeId} AND USER_ID = {userId}")
            .CountAsync(cancellationToken)
            .ConfigureAwait(false);

        if (userRedemptions >= maxPerUser)
        {
            return false;
        }

        // 2. Atomic SQL update on global quota
        var affected = await dbContext.PromoCodes()
            .Where(p => p.PROMO_CODE_ID == promoCodeId && p.REDEEMED_COUNT < p.MAX_REDEMPTIONS && p.IS_ACTIVE)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.REDEEMED_COUNT, p => p.REDEEMED_COUNT + 1), cancellationToken)
            .ConfigureAwait(false);

        if (affected == 0)
        {
            return false;
        }

        // 3. Persist redemption record
        var redemption = PROMO_REDEMPTION.Create(promoCodeId, orderId, userId, clock);
        dbContext.PromoRedemptions().Add(redemption);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return true;
    }

    public Task<int> GetUserRedemptionCountAsync(Guid promoCodeId, Guid userId, CancellationToken cancellationToken) =>
        dbContext.PromoRedemptions().CountAsync(r => r.PROMO_CODE_ID == promoCodeId && r.USER_ID == userId, cancellationToken);

    public async Task RevertRedemptionAsync(Guid promoCodeId, Guid orderId, CancellationToken cancellationToken)
    {
        var redemption = await dbContext.PromoRedemptions()
            .FirstOrDefaultAsync(r => r.PROMO_CODE_ID == promoCodeId && r.ORDER_ID == orderId, cancellationToken)
            .ConfigureAwait(false);

        if (redemption is not null)
        {
            dbContext.PromoRedemptions().Remove(redemption);
            await dbContext.PromoCodes()
                .Where(p => p.PROMO_CODE_ID == promoCodeId && p.REDEEMED_COUNT > 0)
                .ExecuteUpdateAsync(s => s.SetProperty(p => p.REDEEMED_COUNT, p => p.REDEEMED_COUNT - 1), cancellationToken)
                .ConfigureAwait(false);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task<IReadOnlyList<PROMO_CODE>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
        await dbContext.PromoCodes()
            .OrderByDescending(p => p.STARTS_AT_UTC)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public Task<int> CountAsync(CancellationToken cancellationToken) =>
        dbContext.PromoCodes().CountAsync(cancellationToken);
}
