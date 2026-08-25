using Siri.Modules.Payout.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Payout.Application;

public sealed class InstructorEarningsService(
    IRevenueSplitRepository splitRepo,
    IPayoutBatchRepository batchRepo)
{
    public async Task<InstructorEarningsSummaryResponse> GetEarningsSummaryAsync(Guid instructorId, CancellationToken cancellationToken)
    {
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
        Guid instructorId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var effectivePageSize = pageSize is <= 0 or > 100 ? 20 : pageSize;
        var effectivePage = page <= 0 ? 1 : page;

        var items = await batchRepo.GetPayoutHistoryForInstructorAsync(instructorId, effectivePage, effectivePageSize, cancellationToken).ConfigureAwait(false);
        var totalCount = await batchRepo.CountPayoutHistoryForInstructorAsync(instructorId, cancellationToken).ConfigureAwait(false);

        return PagedResult<InstructorPayoutHistoryItem>.Create(items, totalCount, effectivePage, effectivePageSize);
    }
}
