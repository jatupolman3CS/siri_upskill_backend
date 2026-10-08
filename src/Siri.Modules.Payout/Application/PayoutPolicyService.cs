using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Payout.Domain;

namespace Siri.Modules.Payout.Application;

/// <summary>
/// Read-only answer to "what revenue rules apply to me?" (<c>GET /api/payout/policy</c>). Nothing is calculated or stored here — it only reports the values the
/// money code already uses, so a screen can show them instead of hardcoding numbers that may drift from the server:
/// <list type="bullet">
/// <item>withholding tax, minimum payout and hold days come straight from <see cref="PayoutOptions"/> (what <c>PayoutBatchService</c> applies);</item>
/// <item>the revenue share is the caller's <b>own</b> instructor profile rate when they have one (the same per-instructor figure
/// <c>RevenueSplitContract</c> snapshots into each split), otherwise <see cref="REVENUE_SPLIT.DefaultRevenueSharePercent"/>.</item>
/// </list>
/// The method takes the authenticated <b>user</b> id (from <c>IUserContext</c>, never from the request); the profile that carries the rate is resolved through
/// <see cref="IInstructorProfileReader"/>, so a caller can only ever see the rate of the profile that belongs to their own account.
/// </summary>
public sealed class PayoutPolicyService(
    IInstructorProfileReader instructorProfiles,
    ICatalogPriceContract catalog,
    IOptions<PayoutOptions> options)
{
    public async Task<PayoutPolicyResponse> GetForUserAsync(Guid userId, CancellationToken cancellationToken)
    {
        var revenueSharePercent = REVENUE_SPLIT.DefaultRevenueSharePercent;

        var profileId = await instructorProfiles.GetProfileIdByUserIdAsync(userId, cancellationToken).ConfigureAwait(false);
        if (profileId is { } instructorId)
        {
            var sharePercents = await catalog
                .GetInstructorRevenueSharePercentsAsync([instructorId], cancellationToken)
                .ConfigureAwait(false);

            if (sharePercents.TryGetValue(instructorId, out var ownSharePercent))
            {
                revenueSharePercent = ownSharePercent;
            }
        }

        var payoutOptions = options.Value;

        return new PayoutPolicyResponse(
            revenueSharePercent,
            100m - revenueSharePercent,
            payoutOptions.WithholdingTaxPercent,
            payoutOptions.MinimumPayoutAmount,
            payoutOptions.HoldDays);
    }
}
