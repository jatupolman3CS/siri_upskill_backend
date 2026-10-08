using Siri.Modules.Payout.Application;
using Siri.Modules.Payout.Domain;
using Siri.Modules.Payout.Infrastructure.Contracts;
using Xunit;

namespace Siri.UnitTests.Payout;

/// <summary>
/// <see cref="InstructorRevenueReader"/> (docs/contracts/P11-10-instructor-dashboard-summary.md §2.2): the net of one revenue period for one instructor PROFILE.
/// The filter that decides which rows count lives in one place (<c>RevenueSplitQueries</c>) and is exercised here through the repository's default implementation.
/// </summary>
public sealed class InstructorRevenueReaderTests
{
    private const string Period = "2026-10";

    /// <summary>Overrides nothing optional, so the interface default implementation of the net-amount query is the code under test.</summary>
    private sealed class FakeRevenueSplitRepository : IRevenueSplitRepository
    {
        public readonly List<REVENUE_SPLIT> Splits = [];

        /// <summary>How many times the split table was queried at all.</summary>
        public int QueryCalls { get; private set; }

        public Task<REVENUE_SPLIT?> GetByIdAsync(Guid id, CancellationToken cancellationToken) => Task.FromResult<REVENUE_SPLIT?>(null);

        public Task<REVENUE_SPLIT?> GetByOrderItemIdAsync(Guid orderItemId, CancellationToken cancellationToken) => Task.FromResult<REVENUE_SPLIT?>(null);

        public Task<IReadOnlyList<REVENUE_SPLIT>> GetByOrderItemIdsAsync(IEnumerable<Guid> orderItemIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<REVENUE_SPLIT>>([]);

        public Task<IReadOnlyList<REVENUE_SPLIT>> GetEligibleSplitsForPayoutAsync(DateTime holdCutOffUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<REVENUE_SPLIT>>([]);

        public Task<IReadOnlyList<REVENUE_SPLIT>> GetSplitsByBatchItemIdAsync(Guid batchItemId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<REVENUE_SPLIT>>([]);

        public IQueryable<REVENUE_SPLIT> Query()
        {
            QueryCalls++;
            return Splits.AsQueryable();
        }

        public Task<decimal> GetTotalEarningsAsync(Guid instructorId, CancellationToken cancellationToken) => Task.FromResult(0m);

        public Task<decimal> GetPendingEarningsAsync(Guid instructorId, CancellationToken cancellationToken) => Task.FromResult(0m);

        public void Add(REVENUE_SPLIT revenueSplit) => Splits.Add(revenueSplit);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private static REVENUE_SPLIT Split(Guid instructor, decimal instructorAmount, string period = Period) =>
        REVENUE_SPLIT.Create(Guid.NewGuid(), instructor, instructorAmount / 0.7m, 0m, instructorAmount / 0.7m - instructorAmount, instructorAmount, 70m, period);

    private static (InstructorRevenueReader Reader, FakeRevenueSplitRepository Repo) Arrange()
    {
        var repo = new FakeRevenueSplitRepository();
        return (new InstructorRevenueReader(repo), repo);
    }

    [Fact]
    public async Task GetNetRevenueForPeriodAsync_SumsTheInstructorAmountOfThePeriod()
    {
        var (reader, repo) = Arrange();
        var instructor = Guid.NewGuid();
        repo.Add(Split(instructor, 700m));
        repo.Add(Split(instructor, 1_400m));

        Assert.Equal(2_100m, await reader.GetNetRevenueForPeriodAsync(instructor, Period, CancellationToken.None));
    }

    [Fact]
    public async Task GetNetRevenueForPeriodAsync_ExcludesReversedSplits()
    {
        var (reader, repo) = Arrange();
        var instructor = Guid.NewGuid();
        repo.Add(Split(instructor, 700m));
        var reversed = Split(instructor, 5_000m);
        reversed.Reverse(); // refunded before the payout
        repo.Add(reversed);

        Assert.Equal(700m, await reader.GetNetRevenueForPeriodAsync(instructor, Period, CancellationToken.None));
    }

    [Fact]
    public async Task GetNetRevenueForPeriodAsync_IncludesTheNegativeAdjustmentOfARefundAfterPayout()
    {
        var (reader, repo) = Arrange();
        var instructor = Guid.NewGuid();
        repo.Add(Split(instructor, 700m));
        // A refund after the payout is recorded as a negative row in the period it happened in (the original, paid row stays in its own period).
        repo.Add(REVENUE_SPLIT.CreateAdjustment(Guid.NewGuid(), instructor, -1_000m, 0m, -300m, -700m, 70m, Period));

        Assert.Equal(0m, await reader.GetNetRevenueForPeriodAsync(instructor, Period, CancellationToken.None));
    }

    [Fact]
    public async Task GetNetRevenueForPeriodAsync_CountsOnlyTheAskedInstructorAndTheAskedPeriod()
    {
        var (reader, repo) = Arrange();
        var mine = Guid.NewGuid();
        var someoneElse = Guid.NewGuid();
        repo.Add(Split(mine, 700m));
        repo.Add(Split(someoneElse, 90_000m)); // another instructor in the same month
        repo.Add(Split(mine, 40_000m, "2026-09")); // the same instructor, another month

        Assert.Equal(700m, await reader.GetNetRevenueForPeriodAsync(mine, Period, CancellationToken.None));
        Assert.Equal(40_000m, await reader.GetNetRevenueForPeriodAsync(mine, "2026-09", CancellationToken.None));
    }

    [Fact]
    public async Task GetNetRevenueForPeriodAsync_NothingEarned_IsZero()
    {
        var (reader, _) = Arrange();

        Assert.Equal(0m, await reader.GetNetRevenueForPeriodAsync(Guid.NewGuid(), Period, CancellationToken.None));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task GetNetRevenueForPeriodAsync_BlankPeriod_IsZeroWithoutQueryingTheDatabase(string period)
    {
        var (reader, repo) = Arrange();

        Assert.Equal(0m, await reader.GetNetRevenueForPeriodAsync(Guid.NewGuid(), period, CancellationToken.None));
        Assert.Equal(0, repo.QueryCalls);
    }

    [Fact]
    public async Task GetNetRevenueForPeriodAsync_EmptyInstructorId_IsZeroWithoutQueryingTheDatabase()
    {
        var (reader, repo) = Arrange();

        Assert.Equal(0m, await reader.GetNetRevenueForPeriodAsync(Guid.Empty, Period, CancellationToken.None));
        Assert.Equal(0, repo.QueryCalls);
    }
}
