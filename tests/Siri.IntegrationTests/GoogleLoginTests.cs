using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Identity;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.AnonymizeAccount;
using Siri.Modules.Identity.Features.GoogleLogin;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Notification;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// Exercises the real <see cref="GoogleLoginHandler"/> (and through it <see cref="LoginSessionIssuer"/>)
/// against Testcontainers PostgreSQL/Redis. Only Google itself is faked: <see cref="FakeGoogleVerifier"/>
/// stands in for <see cref="IGoogleIdTokenVerifier"/> and returns whatever identity the test sets, which
/// is exactly what a successfully verified ID token would yield (signature/audience/expiry rules are
/// covered by <c>GoogleIdTokenVerifierTests</c> in the unit suite). Requires Docker, like the other
/// integration tests here.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class GoogleLoginTests : IAsyncLifetime
{
    private const string ClientId = "test-client.apps.googleusercontent.com";

    private readonly ContainersFixture _containers;
    private readonly FakeGoogleVerifier _google = new();
    private ServiceProvider _serviceProvider = null!;

    public GoogleLoginTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    private sealed class FakeGoogleVerifier : IGoogleIdTokenVerifier
    {
        public GoogleIdentity? Next { get; set; }

        public Task<GoogleIdentity?> VerifyAsync(string idToken, CancellationToken cancellationToken) =>
            Task.FromResult(Next);
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
                ["Identity:ExternalLogin:Google:ClientId"] = ClientId,
                ["Email:Provider"] = "Log",
            })
            .Build();

        var services = new ServiceCollection();
        services.AddPersistence(configuration);
        services.AddSharedRedis(configuration);
        services.AddIdentityModule(configuration);
        services.AddNotificationModule(configuration);
        services.AddSingleton<IGoogleIdTokenVerifier>(_google);

        _serviceProvider = services.BuildServiceProvider();

        await using var scope = _serviceProvider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _serviceProvider.DisposeAsync();

    private static GoogleLoginCommand Command() => new("id-token-from-google", null, null);

    private static string NewEmail() => $"google-{Guid.NewGuid():N}@example.test";

    private async Task<Siri.SharedKernel.Result<Siri.Modules.Identity.Features.Login.LoginResult>> SignInAsync(
        GoogleIdentity identity)
    {
        _google.Next = identity;
        await using var scope = _serviceProvider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<GoogleLoginHandler>();

        return await handler.HandleAsync(Command(), "TestAgent/1.0", "203.0.113.20", CancellationToken.None);
    }

    [Fact]
    public async Task SignIn_NewGoogleUser_CreatesActiveLearnerLinksGoogleAndStartsSession()
    {
        var email = NewEmail();
        var subject = $"sub-{Guid.NewGuid():N}";

        var result = await SignInAsync(new GoogleIdentity(subject, email, true, "New Person"));

        Assert.True(result.IsSuccess);
        Assert.NotEmpty(result.Value.AccessToken);
        Assert.NotEmpty(result.Value.RawRefreshToken);

        await using var scope = _serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var normalized = email.ToUpperInvariant();
        var user = await db.Users().Include(u => u.Roles).AsNoTracking().SingleAsync(u => u.NormalizedEmail == normalized);

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.NotNull(user.EmailConfirmedAtUtc);
        Assert.Equal("New Person", user.DisplayName);
        Assert.Contains(user.Roles, r => r.Id == ROLE.LearnerId);
        Assert.True(await db.UserExternalLogins().AnyAsync(l => l.UserId == user.Id && l.ProviderSubject == subject));
        Assert.True(await db.UserSessions().AnyAsync(s => s.UserId == user.Id && s.RevokedAtUtc == null));
        Assert.True(await db.RefreshTokens().AnyAsync(t => t.UserId == user.Id && t.RevokedAtUtc == null));
    }

    private async Task<string?> AvatarOfAsync(string email)
    {
        await using var scope = _serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var normalized = email.ToUpperInvariant();

        return (await db.Users().AsNoTracking().SingleAsync(u => u.NormalizedEmail == normalized)).AvatarUrl;
    }

    [Fact]
    public async Task SignIn_NewGoogleUserWithHttpsPicture_StoresItAsTheAvatar()
    {
        var email = NewEmail();
        const string picture = "https://lh3.googleusercontent.com/a/new-user=s96-c";

        var result = await SignInAsync(new GoogleIdentity($"sub-{Guid.NewGuid():N}", email, true, "Pic Person", picture));

        Assert.True(result.IsSuccess);
        Assert.Equal(picture, await AvatarOfAsync(email));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("http://lh3.googleusercontent.com/a/insecure")]
    [InlineData("javascript:alert(1)")]
    public async Task SignIn_NewGoogleUserWithMissingOrUnusablePicture_StillSucceedsWithNoAvatar(string? picture)
    {
        var email = NewEmail();

        var result = await SignInAsync(new GoogleIdentity($"sub-{Guid.NewGuid():N}", email, true, "No Pic", picture));

        Assert.True(result.IsSuccess); // a bad picture must never fail the sign-in itself
        Assert.Null(await AvatarOfAsync(email));
    }

    [Fact]
    public async Task SignIn_ReturningUserWithoutAvatar_GetsItFilledOnTheNextSignIn()
    {
        var email = NewEmail();
        var subject = $"sub-{Guid.NewGuid():N}";
        Assert.True((await SignInAsync(new GoogleIdentity(subject, email, true, "Later Pic", null))).IsSuccess);
        Assert.Null(await AvatarOfAsync(email));

        const string picture = "https://lh3.googleusercontent.com/a/later=s96-c";
        Assert.True((await SignInAsync(new GoogleIdentity(subject, email, true, "Later Pic", picture))).IsSuccess);

        Assert.Equal(picture, await AvatarOfAsync(email));
    }

    [Fact]
    public async Task SignIn_UserWhoAlreadyHasAnAvatar_NeverGetsItOverwrittenByGoogle()
    {
        var email = NewEmail();
        const string original = "https://cdn.example.test/chosen-by-user.png";
        await using (var seedScope = _serviceProvider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = seedScope.ServiceProvider.GetRequiredService<IClock>();
            var user = USER.Register(email, email.ToUpperInvariant(), "placeholder", "Has Avatar");
            user.ConfirmEmail(clock);
            Assert.True(user.SetAvatarIfMissing(original));
            db.Users().Add(user);
            await db.SaveChangesAsync();
        }

        var result = await SignInAsync(new GoogleIdentity(
            $"sub-{Guid.NewGuid():N}", email, true, "Has Avatar", "https://lh3.googleusercontent.com/a/other=s96-c"));

        Assert.True(result.IsSuccess);
        Assert.Equal(original, await AvatarOfAsync(email));
    }

    [Fact]
    public async Task SignIn_SecondTimeWithSameGoogleAccount_ReusesTheUserInsteadOfCreatingAnother()
    {
        var email = NewEmail();
        var subject = $"sub-{Guid.NewGuid():N}";
        var identity = new GoogleIdentity(subject, email, true, "Repeat Person");

        Assert.True((await SignInAsync(identity)).IsSuccess);
        Assert.True((await SignInAsync(identity)).IsSuccess);

        await using var scope = _serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var normalized = email.ToUpperInvariant();
        Assert.Equal(1, await db.Users().CountAsync(u => u.NormalizedEmail == normalized));
        Assert.Equal(1, await db.UserExternalLogins().CountAsync(l => l.ProviderSubject == subject));
    }

    [Fact]
    public async Task SignIn_EmailAlreadyRegisteredAndConfirmed_LinksGoogleToTheExistingAccount()
    {
        var email = NewEmail();
        Guid existingUserId;
        await using (var seedScope = _serviceProvider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = seedScope.ServiceProvider.GetRequiredService<IClock>();
            var user = USER.Register(email, email.ToUpperInvariant(), "placeholder", "Existing Person");
            user.ConfirmEmail(clock);
            db.Users().Add(user);
            await db.SaveChangesAsync();
            existingUserId = user.Id;
        }

        var subject = $"sub-{Guid.NewGuid():N}";
        var result = await SignInAsync(new GoogleIdentity(subject, email, true, "Existing Person"));

        Assert.True(result.IsSuccess);
        await using var scope = _serviceProvider.CreateAsyncScope();
        var verifyDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.Equal(1, await verifyDb.Users().CountAsync(u => u.NormalizedEmail == email.ToUpperInvariant()));
        Assert.True(await verifyDb.UserExternalLogins().AnyAsync(l => l.UserId == existingUserId && l.ProviderSubject == subject));
    }

    [Fact]
    public async Task SignIn_EmailHeldByAnUnconfirmedAccount_ActivatesItAndKillsTheSquattersPassword()
    {
        var email = NewEmail();
        const string squatterPassword = "Squatter-Password-123!";
        Guid pendingUserId;
        string squatterHash;
        await using (var seedScope = _serviceProvider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var hasher = seedScope.ServiceProvider.GetRequiredService<IUserPasswordHasher>();
            var throwaway = USER.Register(email, email.ToUpperInvariant(), "placeholder", "Squatter");
            squatterHash = hasher.HashPassword(throwaway, squatterPassword);
            var pending = USER.Register(email, email.ToUpperInvariant(), squatterHash, "Squatter");
            db.Users().Add(pending); // never confirmed: PendingEmailConfirmation
            await db.SaveChangesAsync();
            pendingUserId = pending.Id;
        }

        var result = await SignInAsync(new GoogleIdentity($"sub-{Guid.NewGuid():N}", email, true, "Real Owner"));

        Assert.True(result.IsSuccess);
        await using var scope = _serviceProvider.CreateAsyncScope();
        var verifyDb = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var verifyHasher = scope.ServiceProvider.GetRequiredService<IUserPasswordHasher>();
        var user = await verifyDb.Users().AsNoTracking().SingleAsync(u => u.Id == pendingUserId);

        Assert.Equal(UserStatus.Active, user.Status);
        Assert.NotEqual(squatterHash, user.PasswordHash);
        Assert.Equal(
            PasswordVerificationResult.Failed,
            verifyHasher.VerifyPassword(user, user.PasswordHash, squatterPassword));
    }

    [Fact]
    public async Task Anonymize_AfterGoogleSignIn_ErasesTheLinkedProviderAccount()
    {
        var email = NewEmail();
        var subject = $"sub-{Guid.NewGuid():N}";
        Assert.True((await SignInAsync(new GoogleIdentity(subject, email, true, "Erase Me"))).IsSuccess);

        await using var scope = _serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var userId = (await db.Users().AsNoTracking().SingleAsync(u => u.NormalizedEmail == email.ToUpperInvariant())).Id;

        var result = await scope.ServiceProvider.GetRequiredService<AnonymizeAccountHandler>()
            .HandleAsync(new AnonymizeAccountCommand(userId, null, "DELETE"), null, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.False(await db.UserExternalLogins().AnyAsync(l => l.UserId == userId));
        Assert.False(await db.UserExternalLogins().AnyAsync(l => l.ProviderSubject == subject));
    }

    [Fact]
    public async Task SignIn_GoogleEmailNotVerified_IsRefusedAndCreatesNothing()
    {
        var email = NewEmail();

        var result = await SignInAsync(new GoogleIdentity($"sub-{Guid.NewGuid():N}", email, false, "Unverified"));

        Assert.True(result.IsFailure);
        await using var scope = _serviceProvider.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.False(await db.Users().AnyAsync(u => u.NormalizedEmail == email.ToUpperInvariant()));
    }

    [Fact]
    public async Task SignIn_TokenFailsVerification_IsRefused()
    {
        _google.Next = null;
        await using var scope = _serviceProvider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<GoogleLoginHandler>();

        var result = await handler.HandleAsync(Command(), null, null, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    [Fact]
    public async Task SignIn_SuspendedAccount_IsRefusedWithTheSameGenericError()
    {
        var email = NewEmail();
        await using (var seedScope = _serviceProvider.CreateAsyncScope())
        {
            var db = seedScope.ServiceProvider.GetRequiredService<AppDbContext>();
            var clock = seedScope.ServiceProvider.GetRequiredService<IClock>();
            var user = USER.Register(email, email.ToUpperInvariant(), "placeholder", "Suspended Person");
            user.ConfirmEmail(clock);
            user.Suspend("test");
            db.Users().Add(user);
            await db.SaveChangesAsync();
        }

        var suspended = await SignInAsync(new GoogleIdentity($"sub-{Guid.NewGuid():N}", email, true, "Suspended Person"));
        var rejected = await SignInAsync(new GoogleIdentity($"sub-{Guid.NewGuid():N}", NewEmail(), false, "Anyone"));

        Assert.True(suspended.IsFailure);
        Assert.Equal(rejected.Error, suspended.Error);
    }
}
