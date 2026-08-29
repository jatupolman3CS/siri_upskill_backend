using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Payout;
using Siri.Modules.Payout.Application;
using Siri.Modules.Payout.Contracts;
using Siri.Modules.Payout.Domain;
using Siri.Modules.Payout.Infrastructure.Contracts;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Payout;

public sealed class RevenueSplitContractTests
{
    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class FakeCatalogPriceContract : ICatalogPriceContract
    {
        public readonly Dictionary<Guid, decimal> CustomSharePercents = [];

        public Task<IReadOnlyDictionary<Guid, decimal>> GetInstructorRevenueSharePercentsAsync(IEnumerable<Guid> instructorIds, CancellationToken cancellationToken)
        {
            var dict = instructorIds
                .Where(CustomSharePercents.ContainsKey)
                .ToDictionary(id => id, id => CustomSharePercents[id]);
            return Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(dict);
        }

        public Task<IReadOnlyDictionary<Guid, CoursePriceInfo>> GetPublishedCoursePricesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, CoursePriceInfo>>(new Dictionary<Guid, CoursePriceInfo>());

        public Task<bool> IsEpisodeFreePreviewAsync(Guid episodeId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<Guid?> GetCourseIdForEpisodeAsync(Guid episodeId, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);

        public Task<bool> IsInstructorOwnerOfEpisodeAsync(Guid episodeId, Guid instructorUserId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> IsInstructorOwnerOfCourseAsync(Guid courseId, Guid instructorUserId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<int> GetPendingReviewsCountAsync(CancellationToken cancellationToken) => Task.FromResult(0);

        public Task<IReadOnlyDictionary<Guid, string>> GetCourseTitlesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
    }

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
            return Task.FromResult<IReadOnlyList<REVENUE_SPLIT>>(Splits.Values.Where(s => idSet.Contains(s.ORDER_ITEM_ID)).ToList());
        }

        public Task<IReadOnlyList<REVENUE_SPLIT>> GetEligibleSplitsForPayoutAsync(DateTime holdCutOffUtc, CancellationToken cancellationToken)
        {
            var list = Splits.Values
                .Where(s => (s.STATUS == RevenueSplitStatus.Pending || s.STATUS == RevenueSplitStatus.Payable)
                            && s.CreatedAtUtc <= holdCutOffUtc
                            && s.PAYOUT_BATCH_ITEM_ID == null)
                .ToList();
            return Task.FromResult<IReadOnlyList<REVENUE_SPLIT>>(list);
        }

        public Task<IReadOnlyList<REVENUE_SPLIT>> GetSplitsByBatchItemIdAsync(Guid batchItemId, CancellationToken cancellationToken)
        {
            var list = Splits.Values.Where(s => s.PAYOUT_BATCH_ITEM_ID == batchItemId).ToList();
            return Task.FromResult<IReadOnlyList<REVENUE_SPLIT>>(list);
        }

        public IQueryable<REVENUE_SPLIT> Query() => Splits.Values.AsQueryable();

        public Task<decimal> GetTotalEarningsAsync(Guid instructorId, CancellationToken cancellationToken) =>
            Task.FromResult(Splits.Values.Where(s => s.INSTRUCTOR_ID == instructorId && s.STATUS == RevenueSplitStatus.Paid).Sum(s => s.INSTRUCTOR_AMOUNT));

        public Task<decimal> GetPendingEarningsAsync(Guid instructorId, CancellationToken cancellationToken) =>
            Task.FromResult(Splits.Values.Where(s => s.INSTRUCTOR_ID == instructorId && s.STATUS == RevenueSplitStatus.Pending).Sum(s => s.INSTRUCTOR_AMOUNT));

        public void Add(REVENUE_SPLIT revenueSplit) => Splits[revenueSplit.REVENUE_SPLIT_ID] = revenueSplit;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task RecordRevenueSplitsAsync_UsesDynamicSharePercentAndDeductsPaymentFee()
    {
        var repo = new FakeRevenueSplitRepository();
        var catalogPriceContract = new FakeCatalogPriceContract();
        var options = Options.Create(new PayoutOptions { EstimatedPaymentFeePercent = 3.30m });
        var clock = new FakeClock(new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc));

        var instructorWithCustomShare = Guid.NewGuid();
        var instructorWithDefaultShare = Guid.NewGuid();
        catalogPriceContract.CustomSharePercents[instructorWithCustomShare] = 80.00m;

        var contract = new RevenueSplitContract(repo, catalogPriceContract, options, clock);

        var orderId = Guid.NewGuid();
        var item1Id = Guid.NewGuid();
        var item2Id = Guid.NewGuid();

        var items = new List<OrderItemSplitInfo>
        {
            // Item 1: 1,000 THB with custom 80% share, explicit fee of 33 THB
            new(item1Id, instructorWithCustomShare, 1000m, 33.00m),
            // Item 2: 2,000 THB with default 70% share, auto-estimated fee (3.3% of 2000 = 66.00 THB)
            new(item2Id, instructorWithDefaultShare, 2000m, null)
        };

        await contract.RecordRevenueSplitsAsync(orderId, items, CancellationToken.None);

        Assert.Equal(2, repo.Splits.Count);

        // Assert Item 1
        var split1 = repo.Splits.Values.Single(s => s.ORDER_ITEM_ID == item1Id);
        Assert.Equal(1000m, split1.GROSS_AMOUNT);
        Assert.Equal(33.00m, split1.PAYMENT_FEE_AMOUNT);
        Assert.Equal(80.00m, split1.REVENUE_SHARE_PERCENT);
        // netAmount = 1000 - 33 = 967. 80% of 967 = 773.60
        Assert.Equal(773.60m, split1.INSTRUCTOR_AMOUNT);
        // platformAmount = 967 - 773.60 = 193.40
        Assert.Equal(193.40m, split1.PLATFORM_FEE_AMOUNT);
        Assert.Equal("2026-08", split1.PERIOD_KEY);
        Assert.Equal(RevenueSplitStatus.Pending, split1.STATUS);

        // Assert Item 2
        var split2 = repo.Splits.Values.Single(s => s.ORDER_ITEM_ID == item2Id);
        Assert.Equal(2000m, split2.GROSS_AMOUNT);
        Assert.Equal(66.00m, split2.PAYMENT_FEE_AMOUNT); // 3.3% estimated
        Assert.Equal(70.00m, split2.REVENUE_SHARE_PERCENT);
        // netAmount = 2000 - 66 = 1934. 70% of 1934 = 1353.80
        Assert.Equal(1353.80m, split2.INSTRUCTOR_AMOUNT);
        // platformAmount = 1934 - 1353.80 = 580.20
        Assert.Equal(580.20m, split2.PLATFORM_FEE_AMOUNT);
    }

    [Fact]
    public async Task RecordRevenueSplitsAsync_IsIdempotent()
    {
        var repo = new FakeRevenueSplitRepository();
        var catalogPriceContract = new FakeCatalogPriceContract();
        var options = Options.Create(new PayoutOptions());
        var clock = new FakeClock(new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc));

        var contract = new RevenueSplitContract(repo, catalogPriceContract, options, clock);

        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var instructorId = Guid.NewGuid();

        var items = new List<OrderItemSplitInfo>
        {
            new(itemId, instructorId, 1000m, 0m)
        };

        await contract.RecordRevenueSplitsAsync(orderId, items, CancellationToken.None);
        Assert.Single(repo.Splits);

        // Second call with same order item
        await contract.RecordRevenueSplitsAsync(orderId, items, CancellationToken.None);
        Assert.Single(repo.Splits);
    }

    [Fact]
    public async Task ReverseRevenueSplitsForOrderAsync_WhenPendingOrPayable_MarksReversed()
    {
        var repo = new FakeRevenueSplitRepository();
        var catalogPriceContract = new FakeCatalogPriceContract();
        var options = Options.Create(new PayoutOptions());
        var clock = new FakeClock(new DateTime(2026, 8, 25, 0, 0, 0, DateTimeKind.Utc));

        var contract = new RevenueSplitContract(repo, catalogPriceContract, options, clock);

        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var instructorId = Guid.NewGuid();

        var split = REVENUE_SPLIT.Create(itemId, instructorId, 1000m, 0m, 300m, 700m, 70m, "2026-08");
        repo.Add(split);

        await contract.ReverseRevenueSplitsForOrderAsync(orderId, [itemId], CancellationToken.None);

        Assert.Equal(RevenueSplitStatus.Reversed, split.STATUS);
    }

    [Fact]
    public async Task ReverseRevenueSplitsForOrderAsync_WhenAlreadyPaid_CreatesNegativeAdjustmentInCurrentPeriod()
    {
        var repo = new FakeRevenueSplitRepository();
        var catalogPriceContract = new FakeCatalogPriceContract();
        var options = Options.Create(new PayoutOptions());
        var clock = new FakeClock(new DateTime(2026, 9, 10, 0, 0, 0, DateTimeKind.Utc)); // Next month

        var contract = new RevenueSplitContract(repo, catalogPriceContract, options, clock);

        var orderId = Guid.NewGuid();
        var itemId = Guid.NewGuid();
        var instructorId = Guid.NewGuid();
        var batchItemId = Guid.NewGuid();

        var paidSplit = REVENUE_SPLIT.Create(itemId, instructorId, 1000m, 30m, 291m, 679m, 70m, "2026-08");
        paidSplit.MarkPaid(batchItemId);
        repo.Add(paidSplit);

        await contract.ReverseRevenueSplitsForOrderAsync(orderId, [itemId], CancellationToken.None);

        // Original split is untouched (still Paid, period 2026-08)
        Assert.Equal(RevenueSplitStatus.Paid, paidSplit.STATUS);
        Assert.Equal("2026-08", paidSplit.PERIOD_KEY);

        // A new negative adjustment is recorded in current period (2026-09)
        Assert.Equal(2, repo.Splits.Count);
        var adjustment = repo.Splits.Values.Single(s => s.REVENUE_SPLIT_ID != paidSplit.REVENUE_SPLIT_ID);
        Assert.Equal(-1000m, adjustment.GROSS_AMOUNT);
        Assert.Equal(-30m, adjustment.PAYMENT_FEE_AMOUNT);
        Assert.Equal(-291m, adjustment.PLATFORM_FEE_AMOUNT);
        Assert.Equal(-679m, adjustment.INSTRUCTOR_AMOUNT);
        Assert.Equal(70m, adjustment.REVENUE_SHARE_PERCENT);
        Assert.Equal("2026-09", adjustment.PERIOD_KEY);
        Assert.Equal(RevenueSplitStatus.Pending, adjustment.STATUS);
    }
}
