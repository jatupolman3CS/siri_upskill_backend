using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Identity;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.ForgotPassword;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Identity.Features.Refresh;
using Siri.Modules.Identity.Features.ResetPassword;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Notification;
using Siri.Modules.Notification.Infrastructure;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// Exercises the real <see cref="ForgotPasswordHandler"/>/<see cref="ResetPasswordHandler"/> — resolved
/// from the exact same DI wiring <c>Siri.Api/Program.cs</c> uses (<c>AddPersistence</c> +
/// <c>AddIdentityModule</c> + <c>AddNotificationModule</c> + Redis config, the last one needed because
/// <see cref="ResetPasswordHandler"/> depends on <c>ISessionRegistry</c> the same way
/// <see cref="LoginHandler"/>/<see cref="RefreshHandler"/> already do) — against the
/// Testcontainers-managed MSSQL/Redis instances (database.md: "ห้ามใช้ InMemory provider ในเทสต์ — ใช้
/// Testcontainers MSSQL"). Requires Docker locally; see <see cref="ContainersFixture"/>'s own doc
/// comment — if Docker is not running, container startup fails before any test body here runs, which is
/// an environment issue, not a defect in these tests.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class ForgotPasswordAndResetPasswordTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string NewPassword = "New-Correct-Horse-Battery-42";

    private readonly ContainersFixture _containers;
    private ServiceProvider _serviceProvider = null!;

    public ForgotPasswordAndResetPasswordTests(ContainersFixture containers)
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
        var throwaway = USER.Register(email, normalizedEmail, "placeholder", "Test USER");
        var hash = passwordHasher.HashPassword(throwaway, password);
        var user = USER.Register(email, normalizedEmail, hash, "Test USER");
        user.ConfirmEmail(clock);

        dbContext.Users().Add(user);
        await dbContext.SaveChangesAsync();

        return user;
    }

    private static string ExtractRawTokenFromEmailBody(string bodyHtml)
    {
        var rawToken = Regex.Match(bodyHtml, "token=([0-9A-Fa-f]+)").Groups[1].Value;
        Assert.NotEmpty(rawToken);
        return rawToken;
    }

    /// <summary>
    /// The core anti-enumeration property this task's security review is about (same bar
    /// <c>RegisterAndConfirmEmailTests.Register_SameEmailTwice_...</c> already holds Register to):
    /// requesting a reset for a genuinely existing, active email and for one that was never registered
    /// must return byte-for-byte the same response, and only the existing-email branch may actually do
    /// anything (issue a token, queue an email).
    /// </summary>
    [Fact]
    public async Task ForgotPassword_ExistingAndNonexistentEmail_ReturnIdenticalResponseAndOnlyExistingEmailIssuesToken()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ForgotPasswordHandler>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var email = $"exists-{Guid.NewGuid():N}@example.test";
        var user = await CreateActiveUserAsync(scope.ServiceProvider, dbContext, email, KnownPassword);

        var existingResult = await handler.HandleAsync(new ForgotPasswordCommand(email), CancellationToken.None);
        var nonexistentResult = await handler.HandleAsync(
            new ForgotPasswordCommand($"nobody-{Guid.NewGuid():N}@example.test"), CancellationToken.None);

        Assert.True(existingResult.IsSuccess);
        Assert.True(nonexistentResult.IsSuccess);
        Assert.Equal(existingResult.Value.Message, nonexistentResult.Value.Message); // identical response body

        var tokenCount = await dbContext.UserSecurityTokens().AsNoTracking()
            .CountAsync(t => t.UserId == user.Id && t.Purpose == UserSecurityTokenPurpose.PasswordReset);
        Assert.Equal(1, tokenCount); // only the existing-email call actually issued a token

        var outboxCount = await dbContext.EmailOutboxMessages().AsNoTracking()
            .CountAsync(m => m.ToEmail == email && m.TemplateKey == "identity-password-reset");
        Assert.Equal(1, outboxCount); // and only it queued a reset email
    }

    /// <summary>
    /// Anti-enumeration also holds for an account that exists but is not <see cref="UserStatus.Active"/>
    /// (task instruction: only issue a token for a genuinely existing, Active account) — a pending
    /// registration must get the exact same response as a nonexistent email, and must not receive a
    /// reset link either.
    /// </summary>
    [Fact]
    public async Task ForgotPassword_PendingEmailConfirmationAccount_ReturnsSameResponseAsNonexistentEmailAndIssuesNoToken()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<ForgotPasswordHandler>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var email = $"pending-{Guid.NewGuid():N}@example.test";
        var normalizedEmail = email.ToUpperInvariant();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IUserPasswordHasher>();
        var throwaway = USER.Register(email, normalizedEmail, "placeholder", "Test USER");
        var hash = passwordHasher.HashPassword(throwaway, KnownPassword);
        var pendingUser = USER.Register(email, normalizedEmail, hash, "Test USER"); // never confirmed
        dbContext.Users().Add(pendingUser);
        await dbContext.SaveChangesAsync();

        var pendingResult = await handler.HandleAsync(new ForgotPasswordCommand(email), CancellationToken.None);
        var nonexistentResult = await handler.HandleAsync(
            new ForgotPasswordCommand($"nobody-{Guid.NewGuid():N}@example.test"), CancellationToken.None);

        Assert.Equal(nonexistentResult.Value.Message, pendingResult.Value.Message);

        var tokenCount = await dbContext.UserSecurityTokens().AsNoTracking()
            .CountAsync(t => t.UserId == pendingUser.Id && t.Purpose == UserSecurityTokenPurpose.PasswordReset);
        Assert.Equal(0, tokenCount);
    }

    [Fact]
    public async Task ForgotPasswordThenResetPassword_ValidToken_ChangesPasswordSoOldPasswordFailsAndNewPasswordWorksAtLogin()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var forgotHandler = scope.ServiceProvider.GetRequiredService<ForgotPasswordHandler>();
        var resetHandler = scope.ServiceProvider.GetRequiredService<ResetPasswordHandler>();
        var loginHandler = scope.ServiceProvider.GetRequiredService<LoginHandler>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var email = $"reset-{Guid.NewGuid():N}@example.test";
        await CreateActiveUserAsync(scope.ServiceProvider, dbContext, email, KnownPassword);

        await forgotHandler.HandleAsync(new ForgotPasswordCommand(email), CancellationToken.None);
        var outboxMessage = await dbContext.EmailOutboxMessages().AsNoTracking().SingleAsync(m => m.ToEmail == email);
        var rawToken = ExtractRawTokenFromEmailBody(outboxMessage.BodyHtml);

        var resetResult = await resetHandler.HandleAsync(
            new ResetPasswordCommand(rawToken, NewPassword), "203.0.113.10", CancellationToken.None);
        Assert.True(resetResult.IsSuccess);

        var oldPasswordLogin = await loginHandler.HandleAsync(
            new LoginCommand(email, KnownPassword, null, null), null, null, CancellationToken.None);
        Assert.True(oldPasswordLogin.IsFailure);

        var newPasswordLogin = await loginHandler.HandleAsync(
            new LoginCommand(email, NewPassword, null, null), null, null, CancellationToken.None);
        Assert.True(newPasswordLogin.IsSuccess);

        // The reset token itself must now be consumed — replaying it must fail too.
        var replayResult = await resetHandler.HandleAsync(
            new ResetPasswordCommand(rawToken, "Another-Strong-Password-7"), "203.0.113.10", CancellationToken.None);
        Assert.True(replayResult.IsFailure);
    }

    /// <summary>
    /// The real security decision this task makes (task instruction): a successful reset must revoke
    /// every session/refresh token the account had, not just log the password change. Logs in first
    /// (creating a real <see cref="USER_SESSION"/> + <see cref="REFRESH_TOKEN"/>), resets the password,
    /// then proves the pre-reset refresh token no longer works — the same proof
    /// <c>LoginAndRefreshTests.Refresh_ValidToken_...</c> uses for rotation, applied to this new
    /// revocation path.
    /// </summary>
    [Fact]
    public async Task ResetPassword_Success_RevokesPreExistingSessionSoOldRefreshTokenNoLongerWorks()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var forgotHandler = scope.ServiceProvider.GetRequiredService<ForgotPasswordHandler>();
        var resetHandler = scope.ServiceProvider.GetRequiredService<ResetPasswordHandler>();
        var loginHandler = scope.ServiceProvider.GetRequiredService<LoginHandler>();
        var refreshHandler = scope.ServiceProvider.GetRequiredService<RefreshHandler>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var email = $"revoke-{Guid.NewGuid():N}@example.test";
        var user = await CreateActiveUserAsync(scope.ServiceProvider, dbContext, email, KnownPassword);

        var loginResult = await loginHandler.HandleAsync(
            new LoginCommand(email, KnownPassword, "device-1", "Test Device"), "UA", "203.0.113.10", CancellationToken.None);
        Assert.True(loginResult.IsSuccess);
        var preResetRawRefreshToken = loginResult.Value.RawRefreshToken;

        var session = await dbContext.UserSessions().AsNoTracking().SingleAsync(s => s.UserId == user.Id);
        Assert.True(session.IsActive);

        await forgotHandler.HandleAsync(new ForgotPasswordCommand(email), CancellationToken.None);
        var outboxMessage = await dbContext.EmailOutboxMessages().AsNoTracking()
            .Where(m => m.ToEmail == email && m.TemplateKey == "identity-password-reset")
            .SingleAsync();
        var rawToken = ExtractRawTokenFromEmailBody(outboxMessage.BodyHtml);

        var resetResult = await resetHandler.HandleAsync(
            new ResetPasswordCommand(rawToken, NewPassword), "203.0.113.10", CancellationToken.None);
        Assert.True(resetResult.IsSuccess);

        var sessionAfterReset = await dbContext.UserSessions().AsNoTracking().SingleAsync(s => s.Id == session.Id);
        Assert.False(sessionAfterReset.IsActive);
        Assert.Equal("password_reset", sessionAfterReset.RevokeReason);

        // The pre-reset refresh token must no longer redeem — the account-takeover-mitigation property
        // this whole design decision exists for.
        var refreshAttempt = await refreshHandler.HandleAsync(
            new RefreshCommand(preResetRawRefreshToken, "UA", "203.0.113.10"), CancellationToken.None);
        Assert.True(refreshAttempt.IsFailure);
    }

    [Fact]
    public async Task ResetPassword_Success_WritesDistinctSecurityAuditEntryAndQueuesPasswordChangedEmail()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var forgotHandler = scope.ServiceProvider.GetRequiredService<ForgotPasswordHandler>();
        var resetHandler = scope.ServiceProvider.GetRequiredService<ResetPasswordHandler>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var email = $"audit-{Guid.NewGuid():N}@example.test";
        var user = await CreateActiveUserAsync(scope.ServiceProvider, dbContext, email, KnownPassword);

        await forgotHandler.HandleAsync(new ForgotPasswordCommand(email), CancellationToken.None);
        var resetOutboxMessage = await dbContext.EmailOutboxMessages().AsNoTracking()
            .Where(m => m.ToEmail == email && m.TemplateKey == "identity-password-reset")
            .SingleAsync();
        var rawToken = ExtractRawTokenFromEmailBody(resetOutboxMessage.BodyHtml);

        var resetResult = await resetHandler.HandleAsync(
            new ResetPasswordCommand(rawToken, NewPassword), "203.0.113.10", CancellationToken.None);
        Assert.True(resetResult.IsSuccess);

        var audit = await dbContext.SecurityAudits().AsNoTracking()
            .SingleAsync(a => a.UserId == user.Id && a.EventType == "password.reset_succeeded");
        Assert.Equal("203.0.113.10", audit.IpAddress);

        var notificationCount = await dbContext.EmailOutboxMessages().AsNoTracking()
            .CountAsync(m => m.ToEmail == email && m.TemplateKey == "identity-password-changed");
        Assert.Equal(1, notificationCount);
    }

    /// <summary>
    /// Anti-enumeration parallel to <c>ConfirmEmail_UnknownExpiredAndAlreadyConsumedTokens_...</c>: a
    /// token that was never issued, one that expired, and one that was already consumed must all fail
    /// with the exact same error message — never a distinguishable one.
    /// </summary>
    [Fact]
    public async Task ResetPassword_UnknownExpiredAndAlreadyConsumedTokens_AllReturnTheExactSameErrorMessage()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var forgotHandler = scope.ServiceProvider.GetRequiredService<ForgotPasswordHandler>();
        var resetHandler = scope.ServiceProvider.GetRequiredService<ResetPasswordHandler>();
        var tokenGenerator = scope.ServiceProvider.GetRequiredService<ISecurityTokenGenerator>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        // 1) A token that was never issued at all.
        var (neverIssuedRawToken, _) = tokenGenerator.Generate();
        var unknownResult = await resetHandler.HandleAsync(
            new ResetPasswordCommand(neverIssuedRawToken, NewPassword), null, CancellationToken.None);

        // 2) A token that was issued but is already expired.
        var expiredEmail = $"expired-{Guid.NewGuid():N}@example.test";
        var expiredUser = await CreateActiveUserAsync(scope.ServiceProvider, dbContext, expiredEmail, KnownPassword);
        var (expiredRawToken, expiredHash) = tokenGenerator.Generate();
        dbContext.UserSecurityTokens().Add(
            USER_SECURITY_TOKEN.Issue(expiredUser.Id, UserSecurityTokenPurpose.PasswordReset, expiredHash, clock.UtcNow.AddMinutes(-1)));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var expiredResult = await resetHandler.HandleAsync(
            new ResetPasswordCommand(expiredRawToken, NewPassword), null, CancellationToken.None);

        // 3) A token that was valid but has already been consumed once.
        var consumedEmail = $"consumed-{Guid.NewGuid():N}@example.test";
        await CreateActiveUserAsync(scope.ServiceProvider, dbContext, consumedEmail, KnownPassword);
        await forgotHandler.HandleAsync(new ForgotPasswordCommand(consumedEmail), CancellationToken.None);
        var consumedOutbox = await dbContext.EmailOutboxMessages().AsNoTracking()
            .Where(m => m.ToEmail == consumedEmail && m.TemplateKey == "identity-password-reset")
            .SingleAsync();
        var consumedRawToken = ExtractRawTokenFromEmailBody(consumedOutbox.BodyHtml);
        await resetHandler.HandleAsync(new ResetPasswordCommand(consumedRawToken, NewPassword), null, CancellationToken.None); // consume it once, legitimately
        var alreadyConsumedResult = await resetHandler.HandleAsync(
            new ResetPasswordCommand(consumedRawToken, "Yet-Another-Strong-Password-8"), null, CancellationToken.None);

        Assert.True(unknownResult.IsFailure);
        Assert.True(expiredResult.IsFailure);
        Assert.True(alreadyConsumedResult.IsFailure);

        Assert.Equal(unknownResult.Error.Message, expiredResult.Error.Message);
        Assert.Equal(unknownResult.Error.Message, alreadyConsumedResult.Error.Message);
        Assert.Equal(unknownResult.Error.Code, expiredResult.Error.Code);
        Assert.Equal(unknownResult.Error.Code, alreadyConsumedResult.Error.Code);
    }

    /// <summary>
    /// Proves the design decision documented on <see cref="ForgotPasswordHandler"/> (task instruction:
    /// "verify whichever behavior you actually implemented"): requesting a second reset link
    /// invalidates the first one, so only the newest link ever works.
    /// </summary>
    [Fact]
    public async Task ForgotPassword_RequestedTwice_InvalidatesFirstTokenSoOnlyTheNewestLinkWorks()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var forgotHandler = scope.ServiceProvider.GetRequiredService<ForgotPasswordHandler>();
        var resetHandler = scope.ServiceProvider.GetRequiredService<ResetPasswordHandler>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var email = $"twice-{Guid.NewGuid():N}@example.test";
        await CreateActiveUserAsync(scope.ServiceProvider, dbContext, email, KnownPassword);

        await forgotHandler.HandleAsync(new ForgotPasswordCommand(email), CancellationToken.None);
        var firstOutbox = await dbContext.EmailOutboxMessages().AsNoTracking()
            .Where(m => m.ToEmail == email && m.TemplateKey == "identity-password-reset")
            .OrderBy(m => m.Id) // UUIDv7 ids are chronologically sortable (EMAIL_OUTBOX_MESSAGE's own doc comment)
            .FirstAsync();
        var firstRawToken = ExtractRawTokenFromEmailBody(firstOutbox.BodyHtml);

        await forgotHandler.HandleAsync(new ForgotPasswordCommand(email), CancellationToken.None);
        var secondOutbox = await dbContext.EmailOutboxMessages().AsNoTracking()
            .Where(m => m.ToEmail == email && m.TemplateKey == "identity-password-reset")
            .OrderBy(m => m.Id)
            .Skip(1)
            .SingleAsync();
        var secondRawToken = ExtractRawTokenFromEmailBody(secondOutbox.BodyHtml);

        Assert.NotEqual(firstRawToken, secondRawToken);

        // The older (first-issued) link must have been invalidated by the second request.
        var firstAttempt = await resetHandler.HandleAsync(
            new ResetPasswordCommand(firstRawToken, NewPassword), null, CancellationToken.None);
        Assert.True(firstAttempt.IsFailure);

        // The newest link must still work.
        var secondAttempt = await resetHandler.HandleAsync(
            new ResetPasswordCommand(secondRawToken, NewPassword), null, CancellationToken.None);
        Assert.True(secondAttempt.IsSuccess);
    }
}
