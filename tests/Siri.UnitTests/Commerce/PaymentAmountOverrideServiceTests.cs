using Microsoft.Extensions.Logging.Abstractions;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Commerce;

public sealed class PaymentAmountOverrideServiceTests
{
    private sealed class TickingClock(DateTime start) : IClock
    {
        private DateTime _now = start;

        public DateTime UtcNow => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class InMemoryOverrideRepository : IPaymentAmountOverrideRepository
    {
        public readonly List<PAYMENT_AMOUNT_OVERRIDE> Entries = [];

        private IEnumerable<PAYMENT_AMOUNT_OVERRIDE> Newest =>
            Entries.OrderByDescending(e => e.CHANGED_AT_UTC).ThenByDescending(e => e.PAYMENT_AMOUNT_OVERRIDE_ID);

        public Task<PAYMENT_AMOUNT_OVERRIDE?> GetCurrentAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Newest.FirstOrDefault());

        public Task<int> CountAsync(CancellationToken cancellationToken) => Task.FromResult(Entries.Count);

        public Task<IReadOnlyList<PAYMENT_AMOUNT_OVERRIDE>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PAYMENT_AMOUNT_OVERRIDE>>(Newest.Skip((page - 1) * pageSize).Take(pageSize).ToList());

        public Task AddAsync(PAYMENT_AMOUNT_OVERRIDE entry, CancellationToken cancellationToken)
        {
            Entries.Add(entry);
            return Task.CompletedTask;
        }
    }

    private readonly InMemoryOverrideRepository _repo = new();
    private readonly TickingClock _clock = new(new DateTime(2026, 10, 10, 8, 0, 0, DateTimeKind.Utc));

    private PaymentAmountOverrideService CreateService() =>
        new(_repo, _clock, NullLogger<PaymentAmountOverrideService>.Instance);

    [Fact]
    public async Task GetCurrentAsync_NothingEverConfigured_ReturnsDisabledEmptyState()
    {
        var state = await CreateService().GetCurrentAsync(CancellationToken.None);

        Assert.False(state.IsEnabled);
        Assert.Null(state.OverrideAmount);
        Assert.Null(state.Reason);
        Assert.Null(state.ChangedByUserId);
        Assert.Null(state.ChangedAtUtc);
    }

    [Fact]
    public async Task SetAsync_Enable_PersistsAuditRowAttributedToTheActingAdmin()
    {
        var admin = Guid.NewGuid();

        var result = await CreateService().SetAsync(admin, new SetPaymentAmountOverrideCommand(true, 20m, "live smoke test"), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.True(result.Value.IsEnabled);
        Assert.Equal(20m, result.Value.OverrideAmount);
        Assert.Equal(admin, result.Value.ChangedByUserId);
        var row = Assert.Single(_repo.Entries);
        Assert.Equal(admin, row.CHANGED_BY_USER_ID);
        Assert.Equal("live smoke test", row.REASON);
    }

    [Fact]
    public async Task SetAsync_EveryChangeAppendsARow_NothingIsOverwritten()
    {
        var service = CreateService();
        var admin = Guid.NewGuid();

        await service.SetAsync(admin, new SetPaymentAmountOverrideCommand(true, 20m, "first"), CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(5));
        await service.SetAsync(admin, new SetPaymentAmountOverrideCommand(true, 50m, "raised"), CancellationToken.None);
        _clock.Advance(TimeSpan.FromMinutes(5));
        await service.SetAsync(admin, new SetPaymentAmountOverrideCommand(false, 50m, "done testing"), CancellationToken.None);

        Assert.Equal(3, _repo.Entries.Count);
        var current = await service.GetCurrentAsync(CancellationToken.None);
        Assert.False(current.IsEnabled);        // newest wins
        Assert.Equal(50m, current.OverrideAmount); // amount kept so the form can be prefilled
        Assert.Equal("done testing", current.Reason);
    }

    [Fact]
    public async Task ListHistoryAsync_ReturnsNewestFirstAndPaginates()
    {
        var service = CreateService();
        var admin = Guid.NewGuid();
        for (var i = 1; i <= 5; i++)
        {
            await service.SetAsync(admin, new SetPaymentAmountOverrideCommand(true, i * 10m, $"change {i}"), CancellationToken.None);
            _clock.Advance(TimeSpan.FromMinutes(1));
        }

        var page1 = await service.ListHistoryAsync(1, 2, CancellationToken.None);
        var page3 = await service.ListHistoryAsync(3, 2, CancellationToken.None);

        Assert.Equal(5, page1.TotalCount);
        Assert.Equal(3, page1.TotalPages);
        Assert.Equal(new[] { "change 5", "change 4" }, page1.Items.Select(i => i.Reason).ToArray());
        Assert.Equal(new[] { "change 1" }, page3.Items.Select(i => i.Reason).ToArray());
    }

    [Theory]
    [InlineData(0, 0, 1, 20)]
    [InlineData(-3, -1, 1, 20)]
    [InlineData(2, 100000, 2, 100)]
    public async Task ListHistoryAsync_ClampsPagingInputs(int page, int pageSize, int expectedPage, int expectedPageSize)
    {
        var result = await CreateService().ListHistoryAsync(page, pageSize, CancellationToken.None);

        Assert.Equal(expectedPage, result.Page);
        Assert.Equal(expectedPageSize, result.PageSize);
    }
}
