using Siri.Modules.Cms.Application;
using Siri.Modules.Cms.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Cms;

public sealed class FeatureFlagTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private sealed class FakeFeatureFlagRepository : IFeatureFlagRepository
    {
        public readonly Dictionary<string, FEATURE_FLAG> Flags = [];

        public Task<FEATURE_FLAG?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Flags.Values.FirstOrDefault(f => f.FEATURE_FLAG_ID == id));

        public Task<FEATURE_FLAG?> GetByKeyAsync(string key, CancellationToken cancellationToken) =>
            Task.FromResult(Flags.TryGetValue(key.ToLowerInvariant(), out var f) ? f : null);

        public Task<IReadOnlyList<FEATURE_FLAG>> GetAllAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FEATURE_FLAG>>(Flags.Values.ToList());

        public Task<IReadOnlyList<FEATURE_FLAG>> GetEnabledAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<FEATURE_FLAG>>(Flags.Values.Where(f => f.IS_ENABLED).ToList());

        public void Add(FEATURE_FLAG flag) => Flags[flag.KEY] = flag;

        public void Remove(FEATURE_FLAG flag) => Flags.Remove(flag.KEY);

        public Task<int> SaveChangesAsync(CancellationToken cancellationToken) => Task.FromResult(1);
    }

    [Fact]
    public async Task UpsertFlag_NewFlag_CreatesSuccessfully()
    {
        var repo = new FakeFeatureFlagRepository();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new FeatureFlagService(repo, clock);

        var result = await service.UpsertFlagAsync(
            "ai_tutor",
            new UpsertFeatureFlagRequest("AI Tutor Assistant", "Enable AI chat assistant in classroom", true),
            CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal("ai_tutor", result.Value.Key);
        Assert.True(result.Value.IsEnabled);
        Assert.Equal("AI Tutor Assistant", result.Value.Name);
    }

    [Fact]
    public async Task ToggleFlag_ExistingFlag_UpdatesState()
    {
        var repo = new FakeFeatureFlagRepository();
        var clock = new FakeClock(DateTime.UtcNow);
        var service = new FeatureFlagService(repo, clock);

        await service.UpsertFlagAsync("quiz_mode", new UpsertFeatureFlagRequest("Quiz", null, true), CancellationToken.None);

        var toggleResult = await service.ToggleFlagAsync("quiz_mode", false, CancellationToken.None);

        Assert.True(toggleResult.IsSuccess);
        Assert.False(toggleResult.Value.IsEnabled);
    }
}
