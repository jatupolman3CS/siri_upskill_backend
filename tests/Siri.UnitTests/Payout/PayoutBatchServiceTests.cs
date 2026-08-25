using Siri.Modules.Payout.Application;
using Siri.Modules.Payout.Domain;
using Xunit;

namespace Siri.UnitTests.Payout;

public sealed class PayoutBatchServiceTests
{
    private sealed class FakePayoutBatchRepository : IPayoutBatchRepository
    {
        public readonly Dictionary<Guid, PAYOUT_BATCH> Batches = [];

        public Task<PAYOUT_BATCH?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Batches.TryGetValue(id, out var batch) ? batch : null);

        public IQueryable<PAYOUT_BATCH> Query() => Batches.Values.AsQueryable();

        public Task<IReadOnlyList<InstructorPayoutHistoryItem>> GetPayoutHistoryForInstructorAsync(
            Guid instructorId,
            int page,
            int pageSize,
            CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<InstructorPayoutHistoryItem>>([]);

        public Task<int> CountPayoutHistoryForInstructorAsync(Guid instructorId, CancellationToken cancellationToken) =>
            Task.FromResult(0);

        public void Add(PAYOUT_BATCH batch) => Batches[batch.PAYOUT_BATCH_ID] = batch;

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakePayoutBatchItemRepository : IPayoutBatchItemRepository
    {
        public readonly List<PAYOUT_BATCH_ITEM> Items = [];

        public Task<IReadOnlyList<PAYOUT_BATCH_ITEM>> GetByBatchIdAsync(Guid batchId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PAYOUT_BATCH_ITEM>>(Items.Where(i => i.BATCH_ID == batchId).ToList());

        public IQueryable<PAYOUT_BATCH_ITEM> Query() => Items.AsQueryable();

        public void AddRange(IEnumerable<PAYOUT_BATCH_ITEM> items) => Items.AddRange(items);
    }

    [Fact]
    public async Task CreateAsync_CreatesDraftBatch()
    {
        var batchRepo = new FakePayoutBatchRepository();
        var itemRepo = new FakePayoutBatchItemRepository();
        var service = new PayoutBatchService(batchRepo, itemRepo);

        var command = new CreatePayoutBatchCommand("2026-08");

        var result = await service.CreateAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("2026-08", result.Value.PeriodKey);
        Assert.Equal(PayoutBatchStatus.Draft, result.Value.Status);
        Assert.Equal(0m, result.Value.TotalAmount);
        Assert.Empty(result.Value.Items);
    }

    [Fact]
    public async Task GetByIdAsync_WhenExistsWithItems_ReturnsBatchWithItems()
    {
        var batchRepo = new FakePayoutBatchRepository();
        var itemRepo = new FakePayoutBatchItemRepository();
        var service = new PayoutBatchService(batchRepo, itemRepo);

        var batch = PAYOUT_BATCH.Create("2026-08");
        var instructorId = Guid.NewGuid();
        batch.AddItem(instructorId, 1000m, 30m, 970m);
        batchRepo.Add(batch);

        var result = await service.GetByIdAsync(batch.PAYOUT_BATCH_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(970m, result.Value.TotalAmount);
        Assert.Single(result.Value.Items);
        Assert.Equal(instructorId, result.Value.Items[0].InstructorId);
        Assert.Equal(970m, result.Value.Items[0].NetAmount);
    }
}
