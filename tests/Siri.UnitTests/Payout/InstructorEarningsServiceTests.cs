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
            Task.FromResult(Splits.Values.FirstOrDefault(s => s.ORDER_ITEM_ID == orderItemId && s.STATUS != RevenueSplitStatus.Reversed));

        public Task<IReadOnlyList<REVENUE_SPLIT>> GetByOrderItemIdsAsync(IEnumerable<Guid> orderItemIds, CancellationToken cancellationToken)
        {
            var idSet = orderItemIds.ToHashSet();
            var list = Splits.Values.Where(s => idSet.Contains(s.ORDER_ITEM_ID)).ToList();
            return Task.FromResult<IReadOnlyList<REVENUE_SPLIT>>(list);
        }

        public Task<IReadOnlyList<REVENUE_SPLIT>> GetEligibleSplitsForPayoutAsync(DateTime holdCutOffUtc, CancellationToken cancellationToken)
        {
            var list = Splits.Values.Where(s => (s.STATUS == RevenueSplitStatus.Pending || s.STATUS == RevenueSplitStatus.Payable) && s.CreatedAtUtc <= holdCutOffUtc).ToList();
            return Task.FromResult<IReadOnlyList<REVENUE_SPLIT>>(list);
        }

        public Task<IReadOnlyList<REVENUE_SPLIT>> GetSplitsByBatchItemIdAsync(Guid batchItemId, CancellationToken cancellationToken)
        {
            var list = Splits.Values.Where(s => s.PAYOUT_BATCH_ITEM_ID == batchItemId).ToList();
            return Task.FromResult<IReadOnlyList<REVENUE_SPLIT>>(list);
        }

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
        public readonly List<Guid> HistoryRequestedFor = [];

        public Task<PAYOUT_BATCH?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Batches.TryGetValue(id, out var batch) ? batch : null);

        public Task<PAYOUT_BATCH?> GetByPeriodKeyAsync(string periodKey, CancellationToken cancellationToken) =>
            Task.FromResult(Batches.Values.FirstOrDefault(b => b.PERIOD_KEY == periodKey));

        public IQueryable<PAYOUT_BATCH> Query() => Batches.Values.AsQueryable();

        public Task<IReadOnlyList<InstructorPayoutHistoryItem>> GetPayoutHistoryForInstructorAsync(
            Guid instructorId,
            int page,
            int pageSize,
            CancellationToken cancellationToken)
        {
            HistoryRequestedFor.Add(instructorId);
            return Task.FromResult<IReadOnlyList<InstructorPayoutHistoryItem>>(HistoryItems);
        }

        public Task<int> CountPayoutHistoryForInstructorAsync(Guid instructorId, CancellationToken cancellationToken)
        {
            HistoryRequestedFor.Add(instructorId);
            return Task.FromResult(HistoryItems.Count);
        }

        public void Add(PAYOUT_BATCH batch) => Batches[batch.PAYOUT_BATCH_ID] = batch;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static InstructorPayoutHistoryItem HistoryItem() => new(
        Guid.NewGuid(),
        "2026-07",
        5000m,
        4850m,
        150m,
        DateTime.UtcNow,
        PayoutBatchItemStatus.Transferred.ToString(),
        DateTime.UtcNow);

    [Fact]
    public async Task GetEarningsSummaryAsync_CalculatesMetricsCorrectly_ForTheProfileOfTheCallingUser()
    {
        var splitRepo = new FakeRevenueSplitRepository();
        var batchRepo = new FakePayoutBatchRepository();
        var profiles = new FakeInstructorProfileReader();

        // The money is keyed by the instructor PROFILE id; the caller arrives as a (different) USER id.
        var userId = Guid.NewGuid();
        var profileId = profiles.Map(userId, Guid.NewGuid());

        splitRepo.Add(REVENUE_SPLIT.Create(Guid.NewGuid(), profileId, 1000m, 30m, 291m, 679m, 70m, "2026-08"));
        splitRepo.Add(REVENUE_SPLIT.Create(Guid.NewGuid(), profileId, 2000m, 60m, 582m, 1358m, 70m, "2026-08"));
        batchRepo.HistoryItems.Add(HistoryItem());

        var service = new InstructorEarningsService(splitRepo, batchRepo, profiles);
        var summary = await service.GetEarningsSummaryAsync(userId, CancellationToken.None);

        Assert.Equal(679m + 1358m, summary.CumulativeEarnings);
        Assert.Equal(679m + 1358m, summary.EstimatedNextPayoutAmount);
        Assert.Equal(4850m, summary.LatestPayoutAmount);
        Assert.Single(summary.History);
        Assert.Equal(4850m, summary.History[0].NetAmount);
        Assert.Equal(new[] { profileId }, batchRepo.HistoryRequestedFor.Distinct()); // the payout history is read for the profile, not the user id
    }

    [Fact]
    public async Task GetEarningsSummaryAsync_NeverReadsMoneyKeyedByTheUserId()
    {
        var splitRepo = new FakeRevenueSplitRepository();
        var batchRepo = new FakePayoutBatchRepository();
        var profiles = new FakeInstructorProfileReader();

        var userId = Guid.NewGuid();
        profiles.Map(userId, Guid.NewGuid());

        // A row whose instructor id equals the caller USER id is not theirs by any contract and must not be summed.
        splitRepo.Add(REVENUE_SPLIT.Create(Guid.NewGuid(), userId, 1000m, 30m, 291m, 679m, 70m, "2026-08"));

        var summary = await new InstructorEarningsService(splitRepo, batchRepo, profiles).GetEarningsSummaryAsync(userId, CancellationToken.None);

        Assert.Equal(0m, summary.CumulativeEarnings);
        Assert.Equal(0m, summary.EstimatedNextPayoutAmount);
    }

    [Fact]
    public async Task GetEarningsSummaryAsync_InstructorBSeesOnlyTheirOwnProfilesMoney()
    {
        var splitRepo = new FakeRevenueSplitRepository();
        var batchRepo = new FakePayoutBatchRepository();
        var profiles = new FakeInstructorProfileReader();

        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();
        var profileA = profiles.Map(userA, Guid.NewGuid());
        var profileB = profiles.Map(userB, Guid.NewGuid());
        splitRepo.Add(REVENUE_SPLIT.Create(Guid.NewGuid(), profileA, 90_000m, 0m, 27_000m, 63_000m, 70m, "2026-08"));
        splitRepo.Add(REVENUE_SPLIT.Create(Guid.NewGuid(), profileB, 1000m, 0m, 300m, 700m, 70m, "2026-08"));

        var summary = await new InstructorEarningsService(splitRepo, batchRepo, profiles).GetEarningsSummaryAsync(userB, CancellationToken.None);

        Assert.Equal(700m, summary.CumulativeEarnings);
    }

    [Fact]
    public async Task GetEarningsSummaryAsync_UserWithoutProfile_IsZeroAndAsksForNoPayoutHistory()
    {
        var splitRepo = new FakeRevenueSplitRepository();
        var batchRepo = new FakePayoutBatchRepository();
        batchRepo.HistoryItems.Add(HistoryItem());

        var summary = await new InstructorEarningsService(splitRepo, batchRepo, new FakeInstructorProfileReader())
            .GetEarningsSummaryAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(0m, summary.CumulativeEarnings);
        Assert.Equal(0m, summary.LatestPayoutAmount);
        Assert.Equal(0m, summary.EstimatedNextPayoutAmount);
        Assert.Empty(summary.History);
        Assert.Empty(batchRepo.HistoryRequestedFor);
    }

    [Fact]
    public async Task GetPayoutHistoryAsync_ReadsTheHistoryOfTheCallersProfile()
    {
        var batchRepo = new FakePayoutBatchRepository();
        batchRepo.HistoryItems.Add(HistoryItem());
        var profiles = new FakeInstructorProfileReader();
        var userId = Guid.NewGuid();
        var profileId = profiles.Map(userId, Guid.NewGuid());

        var page = await new InstructorEarningsService(new FakeRevenueSplitRepository(), batchRepo, profiles)
            .GetPayoutHistoryAsync(userId, 1, 20, CancellationToken.None);

        Assert.Single(page.Items);
        Assert.Equal(new[] { profileId }, batchRepo.HistoryRequestedFor.Distinct());
    }

    [Fact]
    public async Task GetPayoutHistoryAsync_UserWithoutProfile_GetsAnEmptyPage()
    {
        var batchRepo = new FakePayoutBatchRepository();
        batchRepo.HistoryItems.Add(HistoryItem());

        var page = await new InstructorEarningsService(new FakeRevenueSplitRepository(), batchRepo, new FakeInstructorProfileReader())
            .GetPayoutHistoryAsync(Guid.NewGuid(), 1, 20, CancellationToken.None);

        Assert.Empty(page.Items);
        Assert.Equal(0, page.TotalCount);
        Assert.Empty(batchRepo.HistoryRequestedFor);
    }
}
