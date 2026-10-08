using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Payout.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Payout.Application;

/// <summary>
/// The signed-in instructor's own earnings and payout history. Both methods take the authenticated <b>user</b> id; every Payout table is keyed by the
/// instructor <b>profile</b> id (<c>Course.InstructorId</c>), so the user's own profile is resolved first (<see cref="IInstructorProfileReader"/>) — a caller
/// can only ever see the money of the profile that belongs to their account, and a user without a profile sees nothing.
/// </summary>
public sealed class InstructorEarningsService(
    IRevenueSplitRepository splitRepo,
    IPayoutBatchRepository batchRepo,
    IInstructorProfileReader instructorProfiles)
{
    public async Task<InstructorEarningsSummaryResponse> GetEarningsSummaryAsync(Guid instructorUserId, CancellationToken cancellationToken)
    {
        var instructorProfileId = await instructorProfiles.GetProfileIdByUserIdAsync(instructorUserId, cancellationToken).ConfigureAwait(false);
        if (instructorProfileId is not { } instructorId)
        {
            return new InstructorEarningsSummaryResponse(0m, 0m, 0m, []);
        }

        var cumulativeEarnings = await splitRepo.GetTotalEarningsAsync(instructorId, cancellationToken).ConfigureAwait(false);
        var estimatedNextPayout = await splitRepo.GetPendingEarningsAsync(instructorId, cancellationToken).ConfigureAwait(false);

        var historyItems = await batchRepo.GetPayoutHistoryForInstructorAsync(instructorId, 1, 10, cancellationToken).ConfigureAwait(false);

        var latestPayout = historyItems
            .Where(h => h.Status == PayoutBatchItemStatus.Transferred.ToString() || h.Status == "Transferred" || h.Status == "Paid")
            .Select(h => (decimal?)h.NetAmount)
            .FirstOrDefault() ?? 0m;

        return new InstructorEarningsSummaryResponse(
            cumulativeEarnings,
            latestPayout,
            estimatedNextPayout,
            historyItems);
    }

    public async Task<PagedResult<InstructorPayoutHistoryItem>> GetPayoutHistoryAsync(
        Guid instructorUserId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var instructorProfileId = await instructorProfiles.GetProfileIdByUserIdAsync(instructorUserId, cancellationToken).ConfigureAwait(false);
        if (instructorProfileId is not { } instructorId)
        {
            return PagedResult<InstructorPayoutHistoryItem>.Create([], 0, effectivePage, effectivePageSize);
        }

        var items = await batchRepo.GetPayoutHistoryForInstructorAsync(instructorId, effectivePage, effectivePageSize, cancellationToken).ConfigureAwait(false);
        var totalCount = await batchRepo.CountPayoutHistoryForInstructorAsync(instructorId, cancellationToken).ConfigureAwait(false);

        return PagedResult<InstructorPayoutHistoryItem>.Create(items, totalCount, effectivePage, effectivePageSize);
    }
}
