using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Catalog;

public sealed class CatalogRepositoryTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private sealed class FakeCategoryRepository : ICategoryRepository
    {
        public readonly Dictionary<Guid, CATEGORY> Categories = [];

        public Task<IReadOnlyList<CATEGORY>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<CATEGORY>>(Categories.Values.OrderBy(c => c.SortOrder).ToList());

        public Task<CATEGORY?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Categories.TryGetValue(id, out var category) ? category : null);

        public Task<CATEGORY?> GetBySlugAsync(string slug, CancellationToken cancellationToken) =>
            Task.FromResult(Categories.Values.FirstOrDefault(c => c.Slug == slug));

        public Task AddAsync(CATEGORY category, CancellationToken cancellationToken)
        {
            Categories[category.Id] = category;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(CATEGORY category, CancellationToken cancellationToken)
        {
            Categories[category.Id] = category;
            return Task.CompletedTask;
        }

        public Task DeleteAsync(CATEGORY category, CancellationToken cancellationToken)
        {
            Categories.Remove(category.Id);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeWishlistRepository : IWishlistRepository
    {
        public readonly List<WISHLIST_ITEM> Items = [];

        public Task<IReadOnlyList<WISHLIST_ITEM>> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<WISHLIST_ITEM>>(Items.Where(w => w.UserId == userId).ToList());

        public Task<WISHLIST_ITEM?> GetItemAsync(Guid userId, Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult(Items.FirstOrDefault(w => w.UserId == userId && w.CourseId == courseId));

        public Task AddAsync(WISHLIST_ITEM item, CancellationToken cancellationToken)
        {
            Items.Add(item);
            return Task.CompletedTask;
        }

        public Task RemoveAsync(WISHLIST_ITEM item, CancellationToken cancellationToken)
        {
            Items.Remove(item);
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task CategoryRepository_AddAndGetById_ReturnsCategory()
    {
        var repo = new FakeCategoryRepository();
        var category = CATEGORY.Create("tech-slug", "เทคโนโลยี", "Technology", "icon-code", null, 1);
        await repo.AddAsync(category, CancellationToken.None);

        var loaded = await repo.GetByIdAsync(category.Id, CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal("tech-slug", loaded.Slug);
        Assert.Equal("เทคโนโลยี", loaded.NameTh);
    }

    [Fact]
    public async Task CategoryRepository_GetAll_ReturnsSortedCategories()
    {
        var repo = new FakeCategoryRepository();
        var c2 = CATEGORY.Create("cat-2", "หมวด 2", "Cat 2", null, null, 2);
        var c1 = CATEGORY.Create("cat-1", "หมวด 1", "Cat 1", null, null, 1);

        await repo.AddAsync(c2, CancellationToken.None);
        await repo.AddAsync(c1, CancellationToken.None);

        var all = await repo.GetAllAsync(CancellationToken.None);
        Assert.Equal(2, all.Count);
        Assert.Equal("cat-1", all[0].Slug);
        Assert.Equal("cat-2", all[1].Slug);
    }

    [Fact]
    public async Task WishlistRepository_AddAndRemove_WorksProperly()
    {
        var repo = new FakeWishlistRepository();
        var clock = new FakeClock(DateTime.UtcNow);

        var userId = Guid.NewGuid();
        var courseId = Guid.NewGuid();

        var item = WISHLIST_ITEM.Create(userId, courseId, clock);
        await repo.AddAsync(item, CancellationToken.None);

        var loaded = await repo.GetItemAsync(userId, courseId, CancellationToken.None);
        Assert.NotNull(loaded);

        await repo.RemoveAsync(loaded, CancellationToken.None);
        var afterRemove = await repo.GetItemAsync(userId, courseId, CancellationToken.None);
        Assert.Null(afterRemove);
    }
}
