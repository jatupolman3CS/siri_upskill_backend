using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Commerce;

public sealed class FlashSaleServiceTests
{
    private sealed class FakeFlashSaleRepository : IFlashSaleRepository
    {
        public readonly Dictionary<Guid, FLASH_SALE> FlashSales = [];

        public Task<FLASH_SALE?> GetByIdAsync(Guid flashSaleId, CancellationToken cancellationToken) =>
            Task.FromResult(FlashSales.TryGetValue(flashSaleId, out var f) ? f : null);

        public Task AddAsync(FLASH_SALE flashSale, CancellationToken cancellationToken)
        {
            FlashSales[flashSale.FLASH_SALE_ID] = flashSale;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<FLASH_SALE>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
        {
            IReadOnlyList<FLASH_SALE> items = FlashSales.Values
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
            return Task.FromResult(items);
        }

        public Task<IReadOnlyList<FLASH_SALE>> GetActiveFlashSalesAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FLASH_SALE>>(FlashSales.Values.Where(f => f.IS_ACTIVE && f.STARTS_AT_UTC <= nowUtc && f.ENDS_AT_UTC >= nowUtc).ToList());

        public Task<int> CountAsync(CancellationToken cancellationToken) =>
            Task.FromResult(FlashSales.Count);
    }

    [Fact]
    public async Task CreateAsync_WhenValid_CreatesFlashSaleWithItems()
    {
        var repo = new FakeFlashSaleRepository();
        var service = new FlashSaleService(repo);

        var course1 = Guid.NewGuid();
        var now = DateTime.UtcNow;
        var command = new CreateFlashSaleCommand(
            "9.9 Super Sale",
            now,
            now.AddDays(3),
            [new CreateFlashSaleItemCommand(course1, 499m)]);

        var result = await service.CreateAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("9.9 Super Sale", result.Value.Title);
        Assert.Single(result.Value.Items);
        Assert.Equal(499m, result.Value.Items[0].SalePrice);
        Assert.Single(repo.FlashSales);
    }

    [Fact]
    public async Task GetByIdAsync_WhenExists_ReturnsFlashSale()
    {
        var repo = new FakeFlashSaleRepository();
        var service = new FlashSaleService(repo);

        var now = DateTime.UtcNow;
        var sale = FLASH_SALE.Create("Weekend Sale", now, now.AddDays(2));
        repo.FlashSales[sale.FLASH_SALE_ID] = sale;

        var result = await service.GetByIdAsync(sale.FLASH_SALE_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("Weekend Sale", result.Value.Title);
    }

    [Fact]
    public async Task ListAsync_ReturnsPagedFlashSales()
    {
        var repo = new FakeFlashSaleRepository();
        var service = new FlashSaleService(repo);

        var now = DateTime.UtcNow;
        var sale1 = FLASH_SALE.Create("Sale 1", now, now.AddDays(1));
        var sale2 = FLASH_SALE.Create("Sale 2", now, now.AddDays(2));
        repo.FlashSales[sale1.FLASH_SALE_ID] = sale1;
        repo.FlashSales[sale2.FLASH_SALE_ID] = sale2;

        var result = await service.ListAsync(1, 10, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
    }
}
