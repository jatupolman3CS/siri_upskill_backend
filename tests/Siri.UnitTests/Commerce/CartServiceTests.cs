using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Commerce;

public sealed class CartServiceTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private sealed class FakeCartRepository : ICartRepository
    {
        public readonly Dictionary<Guid, CART> Carts = [];

        public Task<CART?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Carts.TryGetValue(userId, out var cart) ? cart : null);

        public Task AddAsync(CART cart, CancellationToken cancellationToken)
        {
            Carts[cart.USER_ID] = cart;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task GetMyCartAsync_WhenNoCartExists_CreatesEmptyCart()
    {
        var repo = new FakeCartRepository();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new CartService(repo, clock);

        var userId = Guid.NewGuid();
        var result = await service.GetMyCartAsync(userId, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(userId, result.Value.UserId);
        Assert.Empty(result.Value.Items);
        Assert.Single(repo.Carts);
    }

    [Fact]
    public async Task AddItemAsync_AddsItemToCart()
    {
        var repo = new FakeCartRepository();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new CartService(repo, clock);

        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();
        var command = new AddCartItemCommand(CartItemType.Course, courseId);

        var result = await service.AddItemAsync(userId, command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Single(result.Value.Items);
        Assert.Equal(courseId, result.Value.Items[0].RefId);
    }

    [Fact]
    public async Task RemoveItemAsync_RemovesItemFromCart()
    {
        var repo = new FakeCartRepository();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new CartService(repo, clock);

        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();

        var addResult = await service.AddItemAsync(userId, new AddCartItemCommand(CartItemType.Course, courseId), CancellationToken.None);
        var cartItemId = addResult.Value.Items[0].Id;

        var removeResult = await service.RemoveItemAsync(userId, cartItemId, CancellationToken.None);

        Assert.True(removeResult.IsSuccess);
        Assert.Empty(removeResult.Value.Items);
    }
}
