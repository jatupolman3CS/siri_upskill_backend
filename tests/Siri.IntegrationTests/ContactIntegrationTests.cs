using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;
using Siri.Api.Authorization;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Identity;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Notification;
using Siri.Modules.Notification.Domain;
using Siri.Modules.Notification.Features.GetContactMessages;
using Siri.Modules.Notification.Features.ResolveContactMessage;
using Siri.Modules.Notification.Features.SubmitContactMessage;
using Siri.Modules.Notification.Infrastructure;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;
using Xunit;

namespace Siri.IntegrationTests;

[Collection(ContainersCollection.Name)]
public sealed class ContactIntegrationTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "contact-integration-tests-signing-key-0123456789";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly ContainersFixture _containers;

    // Unique per test instance: xUnit builds a new instance (and re-runs InitializeAsync) for every test, and all tests
    // share one database, so fixed addresses would hit IX_USERS_NORMALIZED_EMAIL from the second test on.
    private readonly string _adminEmail = $"admin-contact-{Guid.NewGuid():N}@example.test";
    private readonly string _learnerEmail = $"learner-contact-{Guid.NewGuid():N}@example.test";
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    private Guid _adminUserId;
    private Guid _learnerUserId;

    public ContactIntegrationTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        var builder = WebApplication.CreateBuilder();
        builder.WebHost.UseTestServer();

        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = _containers.SqlConnectionString,
            ["Redis:ConnectionString"] = _containers.RedisConnectionString,
            ["Seo:PublicBaseUrl"] = "https://example.test",
            ["Identity:EmailConfirmation:ConfirmEmailUrl"] = "https://example.test/confirm-email",
            ["Identity:PasswordReset:ResetPasswordUrl"] = "https://example.test/reset-password",
            ["Identity:Security:MaxConcurrentSessions"] = "10",
            ["Identity:Jwt:Issuer"] = TestIssuer,
            ["Identity:Jwt:Audience"] = TestAudience,
            ["Identity:Jwt:SigningKey"] = TestSigningKey,
            ["Identity:Jwt:AccessTokenLifetimeMinutes"] = "15",
            ["Email:Provider"] = "Log",
            ["Notification:Contact:SupportEmail"] = "support@contact.example.test",
        });

        builder.Services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidIssuer = TestIssuer,
                    ValidateAudience = true,
                    ValidAudience = TestAudience,
                    ValidateIssuerSigningKey = true,
                    IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(TestSigningKey)),
                    ValidateLifetime = true,
                    ClockSkew = TimeSpan.Zero,
                };
            });

        builder.Services.AddSiriAuthorizationPolicies();
        builder.Services.AddPersistence(builder.Configuration);
        builder.Services.AddSharedRedis(builder.Configuration);
        builder.Services.AddIdentityModule(builder.Configuration);
        builder.Services.AddNotificationModule(builder.Configuration);

        _app = builder.Build();

        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapNotificationEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();

        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
        await SeedUsersAsync(scope.ServiceProvider, dbContext);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task SubmitContactMessage_ValidCommand_ReturnsOkAndPersistsMessageAndEmailOutbox()
    {
        var command = new SubmitContactMessageCommand(
            Name: "Somchai Jaidee",
            Email: "somchai@example.test",
            Subject: "Inquiry about course refunds",
            Message: "Hello, I would like to inquire about the course refund policy.",
            BotField: null);

        var response = await _client.PostAsJsonAsync("/api/contact", command, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<SubmitContactMessageResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.True(result.Success);

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var saved = await db.ContactMessages().FirstOrDefaultAsync(c => c.Email == "somchai@example.test");
        Assert.NotNull(saved);
        Assert.Equal("Inquiry about course refunds", saved.Subject);
        Assert.Equal(ContactMessageStatus.Pending, saved.Status);

        var outbox = await db.EmailOutboxMessages().FirstOrDefaultAsync(e => e.TemplateKey == "contact-notification");
        Assert.NotNull(outbox);
        Assert.Equal("support@contact.example.test", outbox.ToEmail);
    }

    [Fact]
    public async Task SubmitContactMessage_NoSupportInboxConfigured_SavesMessageButQueuesNoEmailToAMadeUpAddress()
    {
        // IOptions<T>.Value is a process-wide singleton instance, so blanking it here is visible to the handler;
        // restored in finally so other tests in this class keep their configured inbox.
        var contactOptions = _app.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<ContactOptions>>().Value;
        var configuredInbox = contactOptions.SupportEmail;
        contactOptions.SupportEmail = string.Empty;

        var unique = Guid.NewGuid().ToString("N");
        try
        {
            var command = new SubmitContactMessageCommand(
                Name: "Somchai Jaidee",
                Email: $"nobox-{unique}@example.test",
                Subject: $"No inbox configured {unique}",
                Message: "Hello, nobody is configured to receive this by email.",
                BotField: null);

            var response = await _client.PostAsJsonAsync("/api/contact", command, JsonOptions);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            using var scope = _app.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            Assert.NotNull(await db.ContactMessages().FirstOrDefaultAsync(c => c.Subject == $"No inbox configured {unique}"));
            Assert.False(await db.EmailOutboxMessages().AnyAsync(e => e.Subject.Contains($"No inbox configured {unique}")));
        }
        finally
        {
            contactOptions.SupportEmail = configuredInbox;
        }
    }

    [Fact]
    public async Task SubmitContactMessage_BotHoneypotTriggered_ReturnsOkWithoutSavingToDb()
    {
        var command = new SubmitContactMessageCommand(
            Name: "Spam Bot",
            Email: "spambot@example.test",
            Subject: "Cheap Pills",
            Message: "Buy now discount pills",
            BotField: "I am a bot");

        var response = await _client.PostAsJsonAsync("/api/contact", command, JsonOptions);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var exists = await db.ContactMessages().AnyAsync(c => c.Email == "spambot@example.test");
        Assert.False(exists);
    }

    [Fact]
    public async Task SubmitContactMessage_InvalidEmail_ReturnsValidationProblem()
    {
        var command = new SubmitContactMessageCommand(
            Name: "Somchai",
            Email: "invalid-email-format",
            Subject: "Test",
            Message: "Test message body that is long enough.",
            BotField: null);

        var response = await _client.PostAsJsonAsync("/api/contact", command, JsonOptions);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetAdminContactMessages_Unauthorized_ReturnsUnauthorizedOrForbidden()
    {
        // Anonymous
        var anonResponse = await _client.GetAsync("/api/admin/contact-messages");
        Assert.Equal(HttpStatusCode.Unauthorized, anonResponse.StatusCode);

        // Learner role
        var learnerToken = await LoginAndGetAccessTokenAsync(_app.Services, _learnerEmail);
        var learnerClient = _app.GetTestClient();
        learnerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", learnerToken);

        var learnerResponse = await learnerClient.GetAsync("/api/admin/contact-messages");
        Assert.Equal(HttpStatusCode.Forbidden, learnerResponse.StatusCode);
    }

    [Fact]
    public async Task GetAdminContactMessages_AdminRole_ReturnsPagedMessages()
    {
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.ContactMessages().Add(new CONTACT_MESSAGE(
                UuidV7.NewId(), "USER A", "usera@example.test", "Question 1", "Detailed question"));
            await db.SaveChangesAsync();
        }

        var adminToken = await LoginAndGetAccessTokenAsync(_app.Services, _adminEmail);
        var adminClient = _app.GetTestClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var response = await adminClient.GetAsync("/api/admin/contact-messages?page=1&pageSize=20");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var paged = await response.Content.ReadFromJsonAsync<PagedResult<ContactMessageListItemResponse>>(JsonOptions);
        Assert.NotNull(paged);
        Assert.True(paged.Items.Count > 0);
    }

    [Fact]
    public async Task ResolveContactMessage_AdminRole_UpdatesStatusAndResolverInfo()
    {
        Guid messageId;
        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var msg = new CONTACT_MESSAGE(UuidV7.NewId(), "USER B", "userb@example.test", "Question 2", "Need help");
            db.ContactMessages().Add(msg);
            await db.SaveChangesAsync();
            messageId = msg.Id;
        }

        var adminToken = await LoginAndGetAccessTokenAsync(_app.Services, _adminEmail);
        var adminClient = _app.GetTestClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var resolveCommand = new ResolveContactMessageCommand("Addressed via email support");
        var response = await adminClient.PostAsJsonAsync($"/api/admin/contact-messages/{messageId}/resolve", resolveCommand, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using (var scope = _app.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var resolved = await db.ContactMessages().FindAsync(messageId);
            Assert.NotNull(resolved);
            Assert.Equal(ContactMessageStatus.Resolved, resolved.Status);
            Assert.Equal(_adminUserId, resolved.ResolvedBy);
            Assert.NotNull(resolved.ResolvedAtUtc);
            Assert.Equal("Addressed via email support", resolved.AdminNotes);
        }
    }

    private static async Task<USER> CreateUserAsync(IServiceProvider services, AppDbContext dbContext, string email, string password)
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

    private static async Task<string> LoginAndGetAccessTokenAsync(IServiceProvider services, string email)
    {
        var loginHandler = services.GetRequiredService<LoginHandler>();
        var result = await loginHandler.HandleAsync(
            new LoginCommand(email, KnownPassword, "device-1", "Test Device"), "UA", "203.0.113.50", CancellationToken.None);

        Assert.True(result.IsSuccess);
        return result.Value.AccessToken;
    }

    private async Task SeedUsersAsync(IServiceProvider services, AppDbContext db)
    {
        var adminUser = await CreateUserAsync(services, db, _adminEmail, KnownPassword);
        adminUser.AssignRole(await db.SeededRoleAsync(ROLE.AdminId));

        var learnerUser = await CreateUserAsync(services, db, _learnerEmail, KnownPassword);
        learnerUser.AssignRole(await db.SeededRoleAsync(ROLE.LearnerId));

        await db.SaveChangesAsync();

        _adminUserId = adminUser.Id;
        _learnerUserId = learnerUser.Id;
    }
}
