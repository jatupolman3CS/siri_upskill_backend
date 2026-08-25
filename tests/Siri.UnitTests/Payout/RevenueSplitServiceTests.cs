using Siri.Modules.Payout.Application;
using Siri.Modules.Payout.Domain;
using Xunit;

namespace Siri.UnitTests.Payout;

public sealed class RevenueSplitServiceTests
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
            Task.FromResult(0m);

        public Task<decimal> GetPendingEarningsAsync(Guid instructorId, CancellationToken cancellationToken) =>
            Task.FromResult(0m);

        public void Add(REVENUE_SPLIT revenueSplit) => Splits[revenueSplit.REVENUE_SPLIT_ID] = revenueSplit;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task CreateAsync_WhenUniqueOrderItem_CreatesSplit()
    {
        var repo = new FakeRevenueSplitRepository();
        var service = new RevenueSplitService(repo);

        var orderItemId = Guid.NewGuid();
        var instructorId = Guid.NewGuid();
        var command = new CreateRevenueSplitCommand(orderItemId, instructorId, 1000m, 30m, 291m, 679m, "2026-08");

        var result = await service.CreateAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(orderItemId, result.Value.OrderItemId);
        Assert.Equal(instructorId, result.Value.InstructorId);
        Assert.Equal(1000m, result.Value.GrossAmount);
        Assert.Equal(679m, result.Value.InstructorAmount);
        Assert.Equal(RevenueSplitStatus.Pending, result.Value.Status);
    }

    [Fact]
    public async Task CreateAsync_WhenDuplicateOrderItem_ReturnsConflict()
    {
        var repo = new FakeRevenueSplitRepository();
        var service = new RevenueSplitService(repo);

        var orderItemId = Guid.NewGuid();
        var instructorId = Guid.NewGuid();
        var command = new CreateRevenueSplitCommand(orderItemId, instructorId, 1000m, 30m, 291m, 679m, "2026-08");

        await service.CreateAsync(command, CancellationToken.None);
        var result2 = await service.CreateAsync(command, CancellationToken.None);

        Assert.False(result2.IsSuccess);
        Assert.Equal("conflict", result2.Error.Code);
    }

    [Fact]
    public async Task GetByIdAsync_WhenExists_ReturnsSplit()
    {
        var repo = new FakeRevenueSplitRepository();
        var service = new RevenueSplitService(repo);

        var orderItemId = Guid.NewGuid();
        var instructorId = Guid.NewGuid();
        var split = REVENUE_SPLIT.Create(orderItemId, instructorId, 1000m, 30m, 291m, 679m, "2026-08");
        repo.Add(split);

        var result = await service.GetByIdAsync(split.REVENUE_SPLIT_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(split.REVENUE_SPLIT_ID, result.Value.Id);
    }
}
