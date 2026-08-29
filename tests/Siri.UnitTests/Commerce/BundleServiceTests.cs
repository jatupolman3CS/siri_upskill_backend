using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Commerce;

public sealed class BundleServiceTests
{
    private sealed class FakeBundleRepository : IBundleRepository
    {
        public readonly Dictionary<Guid, BUNDLE> Bundles = [];

        public Task<BUNDLE?> GetByIdAsync(Guid bundleId, CancellationToken cancellationToken) =>
            Task.FromResult(Bundles.TryGetValue(bundleId, out var b) ? b : null);

        public Task AddAsync(BUNDLE bundle, CancellationToken cancellationToken)
        {
            Bundles[bundle.BUNDLE_ID] = bundle;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<BUNDLE>> ListAsync(int page, int pageSize, CancellationToken cancellationToken)
        {
            IReadOnlyList<BUNDLE> items = Bundles.Values
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToList();
            return Task.FromResult(items);
        }

        public Task<IReadOnlyList<BUNDLE>> GetActiveBundlesAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BUNDLE>>(Bundles.Values.Where(b => b.IS_ACTIVE && (!b.STARTS_AT_UTC.HasValue || b.STARTS_AT_UTC <= nowUtc) && (!b.ENDS_AT_UTC.HasValue || b.ENDS_AT_UTC >= nowUtc)).ToList());

        public Task<IReadOnlyList<BUNDLE>> GetBundlesByCourseIdAsync(Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<BUNDLE>>(Bundles.Values.Where(b => b.IS_ACTIVE && b.BUNDLE_ITEMS.Any(i => i.COURSE_ID == courseId)).ToList());

        public Task<int> CountAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Bundles.Count);
    }

    [Fact]
    public async Task CreateAsync_WhenValid_CreatesBundleWithItems()
    {
        var repo = new FakeBundleRepository();
        var service = new BundleService(repo);

        var course1 = Guid.NewGuid();
        var course2 = Guid.NewGuid();
        var command = new CreateBundleCommand("full-stack-bundle", "Full-stack Web Dev", "Complete bundle", 2990m, [course1, course2]);

        var result = await service.CreateAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("full-stack-bundle", result.Value.Slug);
        Assert.Equal(2990m, result.Value.Price);
        Assert.Equal(2, result.Value.Items.Count);
        Assert.Single(repo.Bundles);
    }

    [Fact]
    public async Task GetByIdAsync_WhenExists_ReturnsBundle()
    {
        var repo = new FakeBundleRepository();
        var service = new BundleService(repo);

        var bundle = BUNDLE.Create("ai-bundle", "AI Mastery", "Description", 1990m);
        repo.Bundles[bundle.BUNDLE_ID] = bundle;

        var result = await service.GetByIdAsync(bundle.BUNDLE_ID, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("AI Mastery", result.Value.Title);
    }

    [Fact]
    public async Task ListAsync_ReturnsPagedBundles()
    {
        var repo = new FakeBundleRepository();
        var service = new BundleService(repo);

        var bundle1 = BUNDLE.Create("b1", "Bundle 1", null, 1000m);
        var bundle2 = BUNDLE.Create("b2", "Bundle 2", null, 2000m);
        repo.Bundles[bundle1.BUNDLE_ID] = bundle1;
        repo.Bundles[bundle2.BUNDLE_ID] = bundle2;

        var result = await service.ListAsync(1, 10, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        Assert.Equal(2, result.Items.Count);
    }
}
