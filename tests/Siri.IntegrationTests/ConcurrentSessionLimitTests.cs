using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Identity;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Identity.Features.Refresh;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Notification;
using Siri.Modules.Notification.Infrastructure;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;
using StackExchange.Redis;

namespace Siri.IntegrationTests;

/// <summary>
/// Exercises SE-03 (P0-17) end to end — the real <see cref="LoginHandler"/> resolved from the exact
/// same DI wiring <c>Siri.Api/Program.cs</c> uses (<c>AddPersistence</c> + <c>AddIdentityModule</c> +
/// <c>AddNotificationModule</c>, the last one needed because eviction queues a real
/// <c>IEmailOutbox</c> message) — against the Testcontainers-managed MSSQL <em>and</em> Redis
/// instances (database.md: "ห้ามใช้ InMemory provider ในเทสต์ — ใช้ Testcontainers MSSQL"; this task
/// extends that same rule to the Redis mirror). Requires Docker locally; see
/// <see cref="ContainersFixture"/>'s own doc comment — if Docker is not running, container startup
/// fails before any test body here runs, which is an environment issue, not a defect in these tests.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class ConcurrentSessionLimitTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";

    private readonly ContainersFixture _containers;
    private ServiceProvider _serviceProvider = null!;

    public ConcurrentSessionLimitTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _containers.SqlConnectionString,
                ["Redis:ConnectionString"] = _containers.RedisConnectionString,
                ["Identity:EmailConfirmation:ConfirmEmailUrl"] = "https://example.test/confirm-email",
                ["Identity:PasswordReset:ResetPasswordUrl"] = "https://example.test/reset-password",
                ["Identity:Security:MaxConcurrentSessions"] = "2", // matches security.md's default explicitly, not relying on the Options default
                ["Identity:Jwt:Issuer"] = "https://api.siriupskill.test",
                ["Identity:Jwt:Audience"] = "siriupskill-frontend-test",
                ["Identity:Jwt:SigningKey"] = new string('k', 64),
                ["Identity:Jwt:AccessTokenLifetimeMinutes"] = "15",
                ["Email:Provider"] = "Log", // never a real SMTP send — matches this task's "do not send real email" constraint
            })
            .Build();

        var services = new ServiceCollection();
        services.AddPersistence(configuration);
        services.AddIdentityModule(configuration);
        services.AddNotificationModule(configuration);

        _serviceProvider = services.BuildServiceProvider();

        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync(); // applies every migration, including this task's AddUserMaxConcurrentSessionsOverride
    }

    public async Task DisposeAsync() => await _serviceProvider.DisposeAsync();

    private static async Task<User> CreateActiveUserAsync(IServiceProvider services, AppDbContext dbContext, string email, string password)
    {
        var passwordHasher = services.GetRequiredService<IUserPasswordHasher>();
        var clock = services.GetRequiredService<IClock>();

        var normalizedEmail = email.ToUpperInvariant();
        var throwaway = User.Register(email, normalizedEmail, "placeholder", "Test User");
        var hash = passwordHasher.HashPassword(throwaway, password);
        var user = User.Register(email, normalizedEmail, hash, "Test User");
        user.ConfirmEmail(clock);

        dbContext.Users().Add(user);
        await dbContext.SaveChangesAsync();

        return user;
    }

    private static RedisKey SessionKey(Guid userId, Guid sessionId) => $"session:{userId}:{sessionId}";

    [Fact]
    public async Task Login_ThirdConcurrentLoginAtDefaultLimit_EvictsOldestSessionWithAuditEmailAndRedisCleanup()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var loginHandler = scope.ServiceProvider.GetRequiredService<LoginHandler>();
        var refreshHandler = scope.ServiceProvider.GetRequiredService<RefreshHandler>();
        var tokenGenerator = scope.ServiceProvider.GetRequiredService<ISecurityTokenGenerator>();

        var email = $"concurrent-{Guid.NewGuid():N}@example.test";
        var user = await CreateActiveUserAsync(scope.ServiceProvider, dbContext, email, KnownPassword);

        var login1 = await loginHandler.HandleAsync(
            new LoginCommand(email, KnownPassword, "device-1", "Device One"), "UA1", "203.0.113.1", CancellationToken.None);
        var login2 = await loginHandler.HandleAsync(
            new LoginCommand(email, KnownPassword, "device-2", "Device Two"), "UA2", "203.0.113.2", CancellationToken.None);
        var login3 = await loginHandler.HandleAsync(
            new LoginCommand(email, KnownPassword, "device-3", "Device Three"), "UA3", "203.0.113.3", CancellationToken.None);

        Assert.True(login1.IsSuccess);
        Assert.True(login2.IsSuccess);
        Assert.True(login3.IsSuccess);

        var session1 = await dbContext.UserSessions().AsNoTracking().SingleAsync(s => s.UserId == user.Id && s.DeviceId == "device-1");
        var session2 = await dbContext.UserSessions().AsNoTracking().SingleAsync(s => s.UserId == user.Id && s.DeviceId == "device-2");
        var session3 = await dbContext.UserSessions().AsNoTracking().SingleAsync(s => s.UserId == user.Id && s.DeviceId == "device-3");

        // The oldest of the first two (device-1) must be the one evicted — device-2 and device-3
        // (the newest, just-created one) must both survive.
        Assert.NotNull(session1.RevokedAtUtc);
        Assert.Equal("concurrent_session_limit_exceeded", session1.RevokeReason);
        Assert.False(session1.IsActive);

        Assert.Null(session2.RevokedAtUtc);
        Assert.True(session2.IsActive);
        Assert.Null(session3.RevokedAtUtc);
        Assert.True(session3.IsActive);

        // The evicted session's refresh token must be revoked ...
        var token1Hash = tokenGenerator.Hash(login1.Value.RawRefreshToken);
        var token1Row = await dbContext.RefreshTokens().AsNoTracking().SingleAsync(t => t.TokenHash == token1Hash);
        Assert.NotNull(token1Row.RevokedAtUtc);
        Assert.Null(token1Row.ReplacedByTokenId); // outright revoke, not a rotation

        // ... so a subsequent Refresh call with it must fail.
        var refreshWithEvictedToken = await refreshHandler.HandleAsync(
            new RefreshCommand(login1.Value.RawRefreshToken, "UA1", "203.0.113.1"), CancellationToken.None);
        Assert.True(refreshWithEvictedToken.IsFailure);

        // Surviving sessions' tokens must still work.
        var refreshWithSurvivingToken = await refreshHandler.HandleAsync(
            new RefreshCommand(login2.Value.RawRefreshToken, "UA2", "203.0.113.2"), CancellationToken.None);
        Assert.True(refreshWithSurvivingToken.IsSuccess);

        // A distinct SecurityAudit row exists for the eviction, referencing the evicted+new session ids.
        var audit = await dbContext.SecurityAudits().AsNoTracking()
            .SingleAsync(a => a.UserId == user.Id && a.EventType == "session.evicted_concurrent_limit");
        Assert.NotNull(audit.Detail);
        Assert.Contains(session1.Id.ToString(), audit.Detail, StringComparison.OrdinalIgnoreCase);
        Assert.Contains(session3.Id.ToString(), audit.Detail, StringComparison.OrdinalIgnoreCase);

        // A notification email was queued to the account owner.
        var outboxMessage = await dbContext.EmailOutboxMessages().AsNoTracking()
            .SingleAsync(m => m.ToEmail == email && m.TemplateKey == "identity-concurrent-session-evicted");
        Assert.Contains("ออกจากระบบ", outboxMessage.Subject, StringComparison.Ordinal);

        // Redis mirror reflects the eviction: the evicted session's key is gone, the two surviving
        // sessions' keys are present.
        await using var redis = await ConnectionMultiplexer.ConnectAsync(_containers.RedisConnectionString);
        var redisDb = redis.GetDatabase();

        Assert.False(await redisDb.KeyExistsAsync(SessionKey(user.Id, session1.Id)));
        Assert.True(await redisDb.KeyExistsAsync(SessionKey(user.Id, session2.Id)));
        Assert.True(await redisDb.KeyExistsAsync(SessionKey(user.Id, session3.Id)));
    }

    [Fact]
    public async Task Login_PerUserOverrideAboveSystemDefault_DoesNotEvictWithinOverride()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var loginHandler = scope.ServiceProvider.GetRequiredService<LoginHandler>();

        var email = $"override-{Guid.NewGuid():N}@example.test";
        var user = await CreateActiveUserAsync(scope.ServiceProvider, dbContext, email, KnownPassword);

        // System default is 2 (test config above) — override this one account to 5. `user` is still
        // tracked by this scope's dbContext (CreateActiveUserAsync added+saved it on this same
        // instance), so mutating it and saving is enough — EF's automatic change detection picks up
        // the property change without an explicit Update() call.
        user.SetMaxConcurrentSessionsOverride(5);
        await dbContext.SaveChangesAsync();

        // Three concurrent logins would evict under the system default, but must NOT evict anything
        // for this account since 3 <= its override of 5.
        var login1 = await loginHandler.HandleAsync(
            new LoginCommand(email, KnownPassword, "device-1", null), "UA1", "203.0.113.11", CancellationToken.None);
        var login2 = await loginHandler.HandleAsync(
            new LoginCommand(email, KnownPassword, "device-2", null), "UA2", "203.0.113.12", CancellationToken.None);
        var login3 = await loginHandler.HandleAsync(
            new LoginCommand(email, KnownPassword, "device-3", null), "UA3", "203.0.113.13", CancellationToken.None);

        Assert.True(login1.IsSuccess);
        Assert.True(login2.IsSuccess);
        Assert.True(login3.IsSuccess);

        var activeSessionCount = await dbContext.UserSessions().AsNoTracking()
            .CountAsync(s => s.UserId == user.Id && s.RevokedAtUtc == null);
        Assert.Equal(3, activeSessionCount);

        var evictionAuditCount = await dbContext.SecurityAudits().AsNoTracking()
            .CountAsync(a => a.UserId == user.Id && a.EventType == "session.evicted_concurrent_limit");
        Assert.Equal(0, evictionAuditCount);

        var evictionEmailCount = await dbContext.EmailOutboxMessages().AsNoTracking()
            .CountAsync(m => m.ToEmail == email && m.TemplateKey == "identity-concurrent-session-evicted");
        Assert.Equal(0, evictionEmailCount);
    }

    [Fact]
    public async Task Login_WithinDefaultLimit_MirrorsSessionIntoRedisWithoutEviction()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var loginHandler = scope.ServiceProvider.GetRequiredService<LoginHandler>();

        var email = $"nomirror-{Guid.NewGuid():N}@example.test";
        var user = await CreateActiveUserAsync(scope.ServiceProvider, dbContext, email, KnownPassword);

        var login = await loginHandler.HandleAsync(
            new LoginCommand(email, KnownPassword, "device-1", null), "UA1", "203.0.113.21", CancellationToken.None);
        Assert.True(login.IsSuccess);

        var session = await dbContext.UserSessions().AsNoTracking().SingleAsync(s => s.UserId == user.Id);
        Assert.True(session.IsActive);

        await using var redis = await ConnectionMultiplexer.ConnectAsync(_containers.RedisConnectionString);
        var redisDb = redis.GetDatabase();

        var key = SessionKey(user.Id, session.Id);
        Assert.True(await redisDb.KeyExistsAsync(key));

        // TTL should be set and roughly match the 30-day refresh-token lifetime (allow generous
        // slack — this is only checking "a real TTL was set", not exact timing).
        var ttl = await redisDb.KeyTimeToLiveAsync(key);
        Assert.NotNull(ttl);
        Assert.True(ttl.Value > TimeSpan.FromDays(29));
        Assert.True(ttl.Value <= TimeSpan.FromDays(30));
    }
}
