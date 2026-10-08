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
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// Exercises the real <see cref="LoginHandler"/>/<see cref="RefreshHandler"/> — resolved from the
/// exact same DI wiring <c>Siri.Api/Program.cs</c> uses (<c>AddPersistence</c> + <c>AddIdentityModule</c>
/// + <c>AddNotificationModule</c> + Redis config) — against the Testcontainers-managed MSSQL/Redis
/// instances (database.md: "ห้ามใช้ InMemory provider ในเทสต์ — ใช้ Testcontainers MSSQL"; this task
/// extends that same rule to the Redis mirror). Requires Docker locally; see
/// <see cref="ContainersFixture"/>'s own doc comment — if Docker is not running, container startup
/// fails before any test body here runs, which is an environment issue, not a defect in these tests.
/// <para>
/// Test users are seeded directly (<see cref="CreateActiveUserAsync"/>: hash a known password,
/// <c>USER.Register</c>, <c>ConfirmEmail</c>, save) rather than going through <c>RegisterHandler</c> —
/// nothing here needs a real registration flow. <c>AddNotificationModule</c> <b>is</b> registered
/// (unlike before P0-17): <see cref="LoginHandler"/> now depends on <c>IEmailOutbox</c> for SE-03's
/// eviction-notification email, so it must be resolvable even though none of the single-login tests in
/// this file trigger an actual eviction (see <c>ConcurrentSessionLimitTests.cs</c> for the tests that
/// do). Same reasoning for the <c>Redis:ConnectionString</c> config value below — <see cref="LoginHandler"/>/
/// <see cref="RefreshHandler"/> now also depend on <c>ISessionRegistry</c> (backed by a real
/// <c>IConnectionMultiplexer</c>), which needs a valid connection string to construct even for tests
/// that don't specifically assert on Redis state.
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class LoginAndRefreshTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";

    private readonly ContainersFixture _containers;
    private ServiceProvider _serviceProvider = null!;

    public LoginAndRefreshTests(ContainersFixture containers)
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
                ["Identity:Security:MaxConcurrentSessions"] = "2",
                ["Identity:Jwt:Issuer"] = "https://api.siriupskill.test",
                ["Identity:Jwt:Audience"] = "siriupskill-frontend-test",
                ["Identity:Jwt:SigningKey"] = new string('k', 64),
                ["Identity:Jwt:AccessTokenLifetimeMinutes"] = "15",
                ["Email:Provider"] = "Log", // never a real SMTP send — matches this task's "do not send real email" constraint
            })
            .Build();

        var services = new ServiceCollection();
        services.AddPersistence(configuration);
        services.AddSharedRedis(configuration);
        services.AddIdentityModule(configuration);
        services.AddNotificationModule(configuration);

        _serviceProvider = services.BuildServiceProvider();

        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync(); // no schema change in this task — still fine to run every migration
    }

    public async Task DisposeAsync() => await _serviceProvider.DisposeAsync();

    private static async Task<USER> CreateActiveUserAsync(IServiceProvider services, AppDbContext dbContext, string email, string password)
    {
        var passwordHasher = services.GetRequiredService<IUserPasswordHasher>();
        var clock = services.GetRequiredService<IClock>();

        var normalizedEmail = email.ToUpperInvariant();
        var user = USER.Register(email, normalizedEmail, "placeholder", "Test USER");
        var hash = passwordHasher.HashPassword(user, password);
        user = USER.Register(email, normalizedEmail, hash, "Test USER");
        user.ConfirmEmail(clock);

        dbContext.Users().Add(user);
        await dbContext.SaveChangesAsync();

        return user;
    }

    [Fact]
    public async Task Login_ValidCredentials_ReturnsAccessTokenAndCreatesSessionRefreshTokenAndAudit()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = scope.ServiceProvider.GetRequiredService<LoginHandler>();
        var tokenGenerator = scope.ServiceProvider.GetRequiredService<ISecurityTokenGenerator>();

        var email = $"login-{Guid.NewGuid():N}@example.test";
        await CreateActiveUserAsync(scope.ServiceProvider, dbContext, email, KnownPassword);

        var command = new LoginCommand(email, KnownPassword, "device-1", "Test Device");
        var result = await handler.HandleAsync(command, "TestAgent/1.0", "203.0.113.10", CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.AccessToken);
        Assert.NotEmpty(result.Value.RawRefreshToken);

        var normalizedEmail = email.ToUpperInvariant();
        var user = await dbContext.Users().AsNoTracking().SingleAsync(u => u.NormalizedEmail == normalizedEmail);
        Assert.NotNull(user.LastLoginAtUtc);

        var session = await dbContext.UserSessions().AsNoTracking().SingleAsync(s => s.UserId == user.Id);
        Assert.Equal("device-1", session.DeviceId);
        Assert.Equal("Test Device", session.DeviceName);
        Assert.Equal("TestAgent/1.0", session.UserAgent);
        Assert.Equal("203.0.113.10", session.IpAddress);
        Assert.True(session.IsActive);

        var refreshTokenHash = tokenGenerator.Hash(result.Value.RawRefreshToken);
        var refreshToken = await dbContext.RefreshTokens().AsNoTracking().SingleAsync(t => t.TokenHash == refreshTokenHash);
        Assert.Equal(user.Id, refreshToken.UserId);
        Assert.Equal(session.Id, refreshToken.SessionId);
        Assert.Null(refreshToken.RevokedAtUtc);

        var audit = await dbContext.SecurityAudits().AsNoTracking()
            .SingleAsync(a => a.UserId == user.Id && a.EventType == "login.succeeded");
        Assert.Equal("203.0.113.10", audit.IpAddress);
    }

    [Fact]
    public async Task Login_WrongPasswordAndNonexistentEmail_ReturnIdenticalErrors()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = scope.ServiceProvider.GetRequiredService<LoginHandler>();

        var email = $"wrongpw-{Guid.NewGuid():N}@example.test";
        await CreateActiveUserAsync(scope.ServiceProvider, dbContext, email, KnownPassword);

        var wrongPasswordResult = await handler.HandleAsync(
            new LoginCommand(email, "definitely-not-the-password", null, null), null, null, CancellationToken.None);

        var nonexistentEmailResult = await handler.HandleAsync(
            new LoginCommand($"nobody-{Guid.NewGuid():N}@example.test", KnownPassword, null, null), null, null, CancellationToken.None);

        Assert.True(wrongPasswordResult.IsFailure);
        Assert.True(nonexistentEmailResult.IsFailure);
        Assert.Equal(wrongPasswordResult.Error.Code, nonexistentEmailResult.Error.Code);
        Assert.Equal(wrongPasswordResult.Error.Message, nonexistentEmailResult.Error.Message);
    }

    [Fact]
    public async Task Login_AccountPendingEmailConfirmation_ReturnsSameGenericErrorAsWrongPassword()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var handler = scope.ServiceProvider.GetRequiredService<LoginHandler>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IUserPasswordHasher>();

        var email = $"pending-{Guid.NewGuid():N}@example.test";
        var normalizedEmail = email.ToUpperInvariant();
        var throwaway = USER.Register(email, normalizedEmail, "placeholder", "Test USER");
        var hash = passwordHasher.HashPassword(throwaway, KnownPassword);
        var pendingUser = USER.Register(email, normalizedEmail, hash, "Test USER"); // never confirmed — stays PendingEmailConfirmation
        dbContext.Users().Add(pendingUser);
        await dbContext.SaveChangesAsync();

        var pendingResult = await handler.HandleAsync(new LoginCommand(email, KnownPassword, null, null), null, null, CancellationToken.None);

        var wrongPasswordEmail = $"wrongpw2-{Guid.NewGuid():N}@example.test";
        await CreateActiveUserAsync(scope.ServiceProvider, dbContext, wrongPasswordEmail, KnownPassword);
        var wrongPasswordResult = await handler.HandleAsync(
            new LoginCommand(wrongPasswordEmail, "wrong-password-entirely", null, null), null, null, CancellationToken.None);

        Assert.True(pendingResult.IsFailure);
        Assert.True(wrongPasswordResult.IsFailure);
        Assert.Equal(wrongPasswordResult.Error.Code, pendingResult.Error.Code);
        Assert.Equal(wrongPasswordResult.Error.Message, pendingResult.Error.Message);
    }

    [Fact]
    public async Task Refresh_ValidToken_RotatesTokenAndOldRawTokenNoLongerWorks()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var loginHandler = scope.ServiceProvider.GetRequiredService<LoginHandler>();
        var refreshHandler = scope.ServiceProvider.GetRequiredService<RefreshHandler>();
        var tokenGenerator = scope.ServiceProvider.GetRequiredService<ISecurityTokenGenerator>();

        var email = $"refresh-{Guid.NewGuid():N}@example.test";
        await CreateActiveUserAsync(scope.ServiceProvider, dbContext, email, KnownPassword);
        var loginResult = await loginHandler.HandleAsync(
            new LoginCommand(email, KnownPassword, "device-1", null), "UA", "203.0.113.10", CancellationToken.None);
        var rawTokenA = loginResult.Value.RawRefreshToken;

        var refreshResult = await refreshHandler.HandleAsync(
            new RefreshCommand(rawTokenA, "UA", "203.0.113.10"), CancellationToken.None);

        Assert.True(refreshResult.IsSuccess);
        Assert.NotEmpty(refreshResult.Value.AccessToken);
        var rawTokenB = refreshResult.Value.RawRefreshToken;
        Assert.NotEqual(rawTokenA, rawTokenB);

        var tokenAHash = tokenGenerator.Hash(rawTokenA);
        var tokenARow = await dbContext.RefreshTokens().AsNoTracking().SingleAsync(t => t.TokenHash == tokenAHash);
        Assert.NotNull(tokenARow.RevokedAtUtc);

        var tokenBHash = tokenGenerator.Hash(rawTokenB);
        var tokenBRow = await dbContext.RefreshTokens().AsNoTracking().SingleAsync(t => t.TokenHash == tokenBHash);
        Assert.Equal(tokenARow.ReplacedByTokenId, tokenBRow.Id);
        Assert.Null(tokenBRow.RevokedAtUtc);
        Assert.Equal(tokenARow.SessionId, tokenBRow.SessionId); // rotation stays within the same session/family

        // The old raw token must no longer work — presenting it again is exactly the reuse scenario,
        // covered in depth by Refresh_ReusedRevokedToken_RevokesEntireFamilyIncludingOtherActiveToken
        // below; here we only assert it is rejected.
        var reuseAttempt = await refreshHandler.HandleAsync(new RefreshCommand(rawTokenA, "UA", "203.0.113.10"), CancellationToken.None);
        Assert.True(reuseAttempt.IsFailure);
    }

    /// <summary>
    /// The specific behavior task P0-16 calls out as "easy to build wrong": reusing an
    /// already-rotated refresh token must revoke the <em>entire</em> token family for that session —
    /// not just the token that was reused. Builds a 3-token rotation chain (A -&gt; B -&gt; C) so that
    /// when A (already revoked, replaced by B) is reused, C is a token that is (a) genuinely still
    /// active and (b) not the one presented — exactly the case a naive "only revoke the presented
    /// token" implementation would fail to catch.
    /// </summary>
    [Fact]
    public async Task Refresh_ReusedRevokedToken_RevokesEntireFamilyIncludingOtherActiveToken()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var loginHandler = scope.ServiceProvider.GetRequiredService<LoginHandler>();
        var refreshHandler = scope.ServiceProvider.GetRequiredService<RefreshHandler>();
        var tokenGenerator = scope.ServiceProvider.GetRequiredService<ISecurityTokenGenerator>();

        var email = $"reuse-{Guid.NewGuid():N}@example.test";
        var user = await CreateActiveUserAsync(scope.ServiceProvider, dbContext, email, KnownPassword);

        var loginResult = await loginHandler.HandleAsync(
            new LoginCommand(email, KnownPassword, "device-1", null), "UA", "203.0.113.10", CancellationToken.None);
        var rawTokenA = loginResult.Value.RawRefreshToken;

        var rotateToB = await refreshHandler.HandleAsync(new RefreshCommand(rawTokenA, "UA", "203.0.113.10"), CancellationToken.None);
        Assert.True(rotateToB.IsSuccess);
        var rawTokenB = rotateToB.Value.RawRefreshToken;

        var rotateToC = await refreshHandler.HandleAsync(new RefreshCommand(rawTokenB, "UA", "203.0.113.10"), CancellationToken.None);
        Assert.True(rotateToC.IsSuccess);
        var rawTokenC = rotateToC.Value.RawRefreshToken;

        var sessionId = (await dbContext.RefreshTokens().AsNoTracking()
            .SingleAsync(t => t.TokenHash == tokenGenerator.Hash(rawTokenA))).SessionId;

        // Reuse the very first token (A) — already revoked when it was rotated into B above.
        var reuseResult = await refreshHandler.HandleAsync(new RefreshCommand(rawTokenA, "203.0.113.99", "203.0.113.99"), CancellationToken.None);
        Assert.True(reuseResult.IsFailure);

        // C was still active (never presented, never rotated) right up until the reuse above — this
        // is the assertion a "only revoke the presented token" bug would fail.
        var tokenCHash = tokenGenerator.Hash(rawTokenC);
        var tokenCRow = await dbContext.RefreshTokens().AsNoTracking().SingleAsync(t => t.TokenHash == tokenCHash);
        Assert.NotNull(tokenCRow.RevokedAtUtc);

        var session = await dbContext.UserSessions().AsNoTracking().SingleAsync(s => s.Id == sessionId);
        Assert.NotNull(session.RevokedAtUtc);
        Assert.False(session.IsActive);

        var reuseAudit = await dbContext.SecurityAudits().AsNoTracking()
            .SingleAsync(a => a.UserId == user.Id && a.EventType == "refresh_token.reuse_detected");
        Assert.NotNull(reuseAudit.Detail);
        Assert.Contains(sessionId.ToString(), reuseAudit.Detail, StringComparison.OrdinalIgnoreCase);

        // C (the still-valid replacement at the time of reuse) must no longer work either, now that
        // the whole family was revoked — not just A.
        var attemptWithC = await refreshHandler.HandleAsync(new RefreshCommand(rawTokenC, "UA", "203.0.113.10"), CancellationToken.None);
        Assert.True(attemptWithC.IsFailure);
    }
}
