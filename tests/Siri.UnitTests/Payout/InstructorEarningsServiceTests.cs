using Siri.Modules.Payout.Application;
using Siri.Modules.Payout.Domain;
using Xunit;

namespace Siri.UnitTests.Payout;

public sealed class InstructorEarningsServiceTests
{
    private sealed class FakeRevenueSplitRepository : IRevenueSplitRepository
    {
        public readonly Dictionary<Guid, REVENUE_SPLIT> Splits = [];

        public Task<REVENUE_SPLIT?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Splits.TryGetValue(id, out var split) ? split : null);

        public Task<REVENUE_SPLIT?> GetByOrderItemIdAsync(Guid orderItemId, CancellationToken cancellationToken) =>
            Task.FromResult(Splits.Values.FirstOrDefault(s => s.ORDER_ITEM_ID == orderItemId));

        public IQueryable<REVENUE_SPLIT> Query() => Splits.Values.AsQueryable();

        public Task<decimal> GetTotalEarningsAsync(Guid instructorId, CancellationToken cancellationToken) =>
            Task.FromResult(Splits.Values
                .Where(s => s.INSTRUCTOR_ID == instructorId)
                .Sum(s => s.INSTRUCTOR_AMOUNT));

        public Task<decimal> GetPendingEarningsAsync(Guid instructorId, CancellationToken cancellationToken) =>
            Task.FromResult(Splits.Values
                .Where(s => s.INSTRUCTOR_ID == instructorId && s.STATUS == RevenueSplitStatus.Pending)
                .Sum(s => s.INSTRUCTOR_AMOUNT));

        public void Add(REVENUE_SPLIT revenueSplit) => Splits[revenueSplit.REVENUE_SPLIT_ID] = revenueSplit;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakePayoutBatchRepository : IPayoutBatchRepository
    {
        public readonly Dictionary<Guid, PAYOUT_BATCH> Batches = [];
        public readonly List<InstructorPayoutHistoryItem> HistoryItems = [];

        public Task<PAYOUT_BATCH?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Batches.TryGetValue(id, out var batch) ? batch : null);

        public IQueryable<PAYOUT_BATCH> Query() => Batches.Values.AsQueryable();

        public Task<IReadOnlyList<InstructorPayoutHistoryItem>> GetPayoutHistoryForInstructorAsync(
            Guid instructorId,
            int page,
            int pageSize,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InstructorPayoutHistoryItem>>(HistoryItems);

        public Task<int> CountPayoutHistoryForInstructorAsync(Guid instructorId, CancellationToken cancellationToken) =>
            Task.FromResult(HistoryItems.Count);

        public void Add(PAYOUT_BATCH batch) => Batches[batch.PAYOUT_BATCH_ID] = batch;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task GetEarningsSummaryAsync_CalculatesMetricsCorrectly()
    {
        var splitRepo = new FakeRevenueSplitRepository();
        var batchRepo = new FakePayoutBatchRepository();

        var instructorId = Guid.NewGuid();

        var split1 = REVENUE_SPLIT.Create(Guid.NewGuid(), instructorId, 1000m, 30m, 291m, 679m, "2026-08");
        var split2 = REVENUE_SPLIT.Create(Guid.NewGuid(), instructorId, 2000m, 60m, 582m, 1358m, "2026-08");
        splitRepo.Add(split1);
        splitRepo.Add(split2);

        batchRepo.HistoryItems.Add(new InstructorPayoutHistoryItem(
            Guid.NewGuid(),
            "2026-07",
            5000m,
            4850m,
            150m,
            DateTime.UtcNow,
            PayoutBatchItemStatus.Transferred.ToString(),
            DateTime.UtcNow));

        var service = new InstructorEarningsService(splitRepo, batchRepo);
        var summary = await service.GetEarningsSummaryAsync(instructorId, CancellationToken.None);

        Assert.Equal(679m + 1358m, summary.CumulativeEarnings);
        Assert.Equal(679m + 1358m, summary.EstimatedNextPayoutAmount);
        Assert.Equal(4850m, summary.LatestPayoutAmount);
        Assert.Single(summary.History);
        Assert.Equal(4850m, summary.History[0].NetAmount);
    }
}
