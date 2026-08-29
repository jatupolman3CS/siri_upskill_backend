using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Identity;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.ConfirmEmail;
using Siri.Modules.Identity.Features.Register;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Notification;
using Siri.Modules.Notification.Infrastructure;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// Exercises the real <see cref="RegisterHandler"/>/<see cref="ConfirmEmailHandler"/> — resolved
/// from the exact same DI wiring <c>Siri.Api/Program.cs</c> uses (<c>AddPersistence</c> +
/// <c>AddIdentityModule</c> + <c>AddNotificationModule</c>) — against the Testcontainers-managed
/// MSSQL instance (database.md: "ห้ามใช้ InMemory provider ในเทสต์ — ใช้ Testcontainers MSSQL").
/// Requires Docker locally; see <see cref="ContainersFixture"/>'s own doc comment — if Docker is not
/// running, container startup fails before any test body here runs, which is an environment issue,
/// not a defect in these tests.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class RegisterAndConfirmEmailTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private ServiceProvider _serviceProvider = null!;

    public RegisterAndConfirmEmailTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = _containers.SqlConnectionString,
                ["Identity:EmailConfirmation:ConfirmEmailUrl"] = "https://example.test/confirm-email",
                ["Identity:PasswordReset:ResetPasswordUrl"] = "https://example.test/reset-password",
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
        await dbContext.Database.MigrateAsync(); // applies every migration, including this task's AddUserSecurityTokens
    }

    public async Task DisposeAsync() => await _serviceProvider.DisposeAsync();

    /// <summary>
    /// The core anti-enumeration property this task's security review is about (task instructions:
    /// "prove through some form of automated test that duplicate registration of the same email does
    /// not return a distinguishable response than a first-time registration"). Registers the same
    /// email twice and asserts: identical response content both times, and — proving the second call
    /// genuinely took the "already exists" branch rather than somehow succeeding twice — only one
    /// <see cref="USER"/> row and one queued confirmation email ever exist for that address.
    /// </summary>
    [Fact]
    public async Task Register_SameEmailTwice_ReturnsIdenticalResponseAndOnlyEverCreatesOneUser()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<RegisterHandler>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var email = $"dup-{Guid.NewGuid():N}@example.test";
        var command = new RegisterCommand(email, "Correct-Horse-Battery-Staple-9", "Student One");

        var firstResult = await handler.HandleAsync(command, CancellationToken.None);
        var secondResult = await handler.HandleAsync(command, CancellationToken.None); // same email again

        Assert.True(firstResult.IsSuccess);
        Assert.True(secondResult.IsSuccess);
        Assert.Equal(firstResult.Value.Message, secondResult.Value.Message); // identical response body

        var normalizedEmail = email.ToUpperInvariant();
        var userCount = await dbContext.Users().AsNoTracking().CountAsync(u => u.NormalizedEmail == normalizedEmail);
        Assert.Equal(1, userCount); // the second call did NOT create a second account

        var outboxCount = await dbContext.EmailOutboxMessages().AsNoTracking().CountAsync(m => m.ToEmail == email);
        Assert.Equal(1, outboxCount); // and did NOT queue a second confirmation email
    }

    [Fact]
    public async Task Register_NewEmail_CreatesPendingUserAndIssuesEmailConfirmationTokenAndQueuesEmail()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<RegisterHandler>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var email = $"new-{Guid.NewGuid():N}@example.test";
        var command = new RegisterCommand(email, "Correct-Horse-Battery-Staple-9", "Student One");

        var result = await handler.HandleAsync(command, CancellationToken.None);

        Assert.True(result.IsSuccess);

        var user = await dbContext.Users().AsNoTracking().SingleAsync(u => u.NormalizedEmail == email.ToUpperInvariant());
        Assert.Equal(UserStatus.PendingEmailConfirmation, user.Status);
        Assert.Null(user.EmailConfirmedAtUtc);

        var token = await dbContext.UserSecurityTokens().AsNoTracking()
            .SingleAsync(t => t.UserId == user.Id && t.Purpose == UserSecurityTokenPurpose.EmailConfirmation);
        Assert.False(token.IsConsumed);
        Assert.True(token.ExpiresAtUtc > DateTime.UtcNow);

        var outboxMessage = await dbContext.EmailOutboxMessages().AsNoTracking().SingleAsync(m => m.ToEmail == email);
        Assert.Contains("ยืนยันอีเมล", outboxMessage.Subject, StringComparison.Ordinal);
        Assert.Contains("https://example.test/confirm-email?token=", outboxMessage.BodyHtml, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RegisterThenConfirmEmail_TokenFromQueuedEmail_ActivatesUserAndConsumesToken()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var registerHandler = scope.ServiceProvider.GetRequiredService<RegisterHandler>();
        var confirmHandler = scope.ServiceProvider.GetRequiredService<ConfirmEmailHandler>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var email = $"confirm-{Guid.NewGuid():N}@example.test";
        await registerHandler.HandleAsync(new RegisterCommand(email, "Correct-Horse-Battery-Staple-9", "Student One"), CancellationToken.None);

        // Read the raw token out of the queued email's HTML exactly like a real user clicking the
        // link would end up submitting it — proves the full pipeline (issue -> hash -> email -> hash
        // again -> match) works end to end, not just that the pieces compile.
        var outboxMessage = await dbContext.EmailOutboxMessages().AsNoTracking().SingleAsync(m => m.ToEmail == email);
        var rawToken = Regex.Match(outboxMessage.BodyHtml, "token=([0-9A-Fa-f]+)").Groups[1].Value;
        Assert.NotEmpty(rawToken);

        var confirmResult = await confirmHandler.HandleAsync(new ConfirmEmailCommand(rawToken), CancellationToken.None);
        Assert.True(confirmResult.IsSuccess);

        var user = await dbContext.Users().AsNoTracking().SingleAsync(u => u.NormalizedEmail == email.ToUpperInvariant());
        Assert.Equal(UserStatus.Active, user.Status);
        Assert.NotNull(user.EmailConfirmedAtUtc);

        var token = await dbContext.UserSecurityTokens().AsNoTracking().SingleAsync(t => t.UserId == user.Id);
        Assert.True(token.IsConsumed);

        // Reusing the same (now-consumed) token must be rejected, not silently re-succeed.
        var replayResult = await confirmHandler.HandleAsync(new ConfirmEmailCommand(rawToken), CancellationToken.None);
        Assert.True(replayResult.IsFailure);
    }

    /// <summary>
    /// Anti-enumeration parallel to <see cref="Register_SameEmailTwice_ReturnsIdenticalResponseAndOnlyEverCreatesOneUser"/>
    /// for ConfirmEmail: a token that was never issued, one that expired, and one that was already
    /// consumed must all fail with the exact same error message — never a distinguishable one (see
    /// <c>ConfirmEmailHandler</c>'s own doc comment).
    /// </summary>
    [Fact]
    public async Task ConfirmEmail_UnknownExpiredAndAlreadyConsumedTokens_AllReturnTheExactSameErrorMessage()
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var registerHandler = scope.ServiceProvider.GetRequiredService<RegisterHandler>();
        var confirmHandler = scope.ServiceProvider.GetRequiredService<ConfirmEmailHandler>();
        var tokenGenerator = scope.ServiceProvider.GetRequiredService<ISecurityTokenGenerator>();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        // 1) A token that was never issued at all.
        var (neverIssuedRawToken, _) = tokenGenerator.Generate();
        var unknownResult = await confirmHandler.HandleAsync(new ConfirmEmailCommand(neverIssuedRawToken), CancellationToken.None);

        // 2) A token that was issued but is already expired.
        var expiredEmail = $"expired-{Guid.NewGuid():N}@example.test";
        await registerHandler.HandleAsync(new RegisterCommand(expiredEmail, "Correct-Horse-Battery-Staple-9", "Student One"), CancellationToken.None);
        var expiredUser = await dbContext.Users().SingleAsync(u => u.NormalizedEmail == expiredEmail.ToUpperInvariant());
        var (expiredRawToken, expiredHash) = tokenGenerator.Generate();
        dbContext.UserSecurityTokens().Add(
            USER_SECURITY_TOKEN.Issue(expiredUser.Id, UserSecurityTokenPurpose.EmailConfirmation, expiredHash, clock.UtcNow.AddMinutes(-1)));
        await dbContext.SaveChangesAsync(CancellationToken.None);
        var expiredResult = await confirmHandler.HandleAsync(new ConfirmEmailCommand(expiredRawToken), CancellationToken.None);

        // 3) A token that was valid but has already been consumed once.
        var consumedEmail = $"consumed-{Guid.NewGuid():N}@example.test";
        await registerHandler.HandleAsync(new RegisterCommand(consumedEmail, "Correct-Horse-Battery-Staple-9", "Student One"), CancellationToken.None);
        var consumedOutbox = await dbContext.EmailOutboxMessages().AsNoTracking().SingleAsync(m => m.ToEmail == consumedEmail);
        var consumedRawToken = Regex.Match(consumedOutbox.BodyHtml, "token=([0-9A-Fa-f]+)").Groups[1].Value;
        await confirmHandler.HandleAsync(new ConfirmEmailCommand(consumedRawToken), CancellationToken.None); // consume it once, legitimately
        var alreadyConsumedResult = await confirmHandler.HandleAsync(new ConfirmEmailCommand(consumedRawToken), CancellationToken.None);

        Assert.True(unknownResult.IsFailure);
        Assert.True(expiredResult.IsFailure);
        Assert.True(alreadyConsumedResult.IsFailure);

        Assert.Equal(unknownResult.Error.Message, expiredResult.Error.Message);
        Assert.Equal(unknownResult.Error.Message, alreadyConsumedResult.Error.Message);
        Assert.Equal(unknownResult.Error.Code, expiredResult.Error.Code);
        Assert.Equal(unknownResult.Error.Code, alreadyConsumedResult.Error.Code);
    }
}
