using Siri.Modules.Identity.Application;
using Siri.Modules.Identity.Domain;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Identity;

public sealed class IdentityRepositoryTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private sealed class FakeUserRepository : IUserRepository
    {
        public readonly Dictionary<Guid, USER> Users = [];

        public Task<USER?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Users.TryGetValue(id, out var user) ? user : null);

        public Task<USER?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult(Users.Values.FirstOrDefault(u => u.Email == email));

        public Task<USER?> GetWithRolesByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Users.TryGetValue(id, out var user) ? user : null);

        public Task<USER?> GetWithRolesByEmailAsync(string email, CancellationToken cancellationToken) =>
            Task.FromResult(Users.Values.FirstOrDefault(u => u.Email == email));

        public Task<(IReadOnlyList<USER> Items, int TotalCount)> GetPagedUsersAsync(int page, int pageSize, string? search, CancellationToken cancellationToken)
        {
            var list = Users.Values.ToList();
            return Task.FromResult<(IReadOnlyList<USER>, int)>((list, list.Count));
        }

        public Task AddAsync(USER user, CancellationToken cancellationToken)
        {
            Users[user.Id] = user;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(USER user, CancellationToken cancellationToken)
        {
            Users[user.Id] = user;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken) => operation();

        public Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken) => operation();
    }

    private sealed class FakeRefreshTokenRepository : IRefreshTokenRepository
    {
        public readonly Dictionary<string, REFRESH_TOKEN> Tokens = [];

        public Task<REFRESH_TOKEN?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken) =>
            Task.FromResult(Tokens.TryGetValue(tokenHash, out var token) ? token : null);

        public Task AddAsync(REFRESH_TOKEN token, CancellationToken cancellationToken)
        {
            Tokens[token.TokenHash] = token;
            return Task.CompletedTask;
        }

        public Task UpdateAsync(REFRESH_TOKEN token, CancellationToken cancellationToken)
        {
            Tokens[token.TokenHash] = token;
            return Task.CompletedTask;
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    [Fact]
    public async Task UserRepository_AddAndGetByEmail_ReturnsUser()
    {
        var repo = new FakeUserRepository();
        var user = USER.Register("test@example.com", "hash_pw", "salt", "Test USER");
        await repo.AddAsync(user, CancellationToken.None);

        var loaded = await repo.GetByEmailAsync("test@example.com", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal("test@example.com", loaded.Email);
        Assert.Equal("Test USER", loaded.DisplayName);
    }

    [Fact]
    public async Task RefreshTokenRepository_AddAndGetByHash_ReturnsToken()
    {
        var repo = new FakeRefreshTokenRepository();
        var clock = new FakeClock(DateTime.UtcNow);

        var userId = Guid.NewGuid();
        var sessionId = Guid.NewGuid();
        var token = REFRESH_TOKEN.Issue(userId, sessionId, "hashed_refresh_token", clock.UtcNow.AddDays(30));
        await repo.AddAsync(token, CancellationToken.None);

        var loaded = await repo.GetByTokenHashAsync("hashed_refresh_token", CancellationToken.None);
        Assert.NotNull(loaded);
        Assert.Equal(userId, loaded.UserId);
    }
}
