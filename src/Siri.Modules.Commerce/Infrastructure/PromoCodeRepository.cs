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
        // Row lock on this user's existing redemptions for this code. Identifiers are double-quoted
        // because the schema is UPPERCASE and PostgreSQL folds unquoted names to lower case (P0-41).
        //
        // NOTE — this is a faithful translation of the previous SQL Server statement, not a fix.
        // That statement used WITH (UPDLOCK, HOLDLOCK), whose key-range lock would also have blocked
        // concurrent INSERTs into the range; FOR UPDATE only locks rows that already exist. Neither
        // version actually closes the TOCTOU window the original comment claimed, because there is no
        // surrounding transaction — OrderService calls TryRedeemAsync and then SaveChangesAsync
        // separately, so every statement here autocommits and any lock is released immediately.
        // Two simultaneous first-time redemptions can therefore both pass the MAX_PER_USER check on
        // either engine. Closing it needs a transaction plus either SERIALIZABLE isolation or a
        // pg_advisory_xact_lock keyed on (promo, user) — a change to money-handling behaviour, which
        // security.md puts outside a provider migration. Tracked as X-26 in docs/TASKS.md.
        // The global quota in step 2 is unaffected: that one is a single atomic UPDATE.
        var userRedemptions = await dbContext.PromoRedemptions()
            .FromSqlInterpolated($@"SELECT * FROM ""COMMERCE"".""PROMO_REDEMPTIONS"" WHERE ""PROMO_CODE_ID"" = {promoCodeId} AND ""USER_ID"" = {userId} FOR UPDATE")
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
