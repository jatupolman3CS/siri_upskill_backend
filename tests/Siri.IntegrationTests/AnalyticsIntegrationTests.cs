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
using Siri.Modules.Analytics;
using Siri.Modules.Analytics.Application;
using Siri.Modules.Analytics.Domain;
using Siri.Modules.Analytics.Features.AdminDashboardSummary;
using Siri.Modules.Analytics.Infrastructure;
using Siri.Modules.Catalog;
using Siri.Modules.Commerce;
using Siri.Modules.Identity;
using Siri.Modules.Learning;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Notification;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// Integration tests for Analytics module (C5).
/// Verifies:
/// 1. AnalyticsRollupJob execution.
/// 2. Admin dashboard summary authorization and data aggregation.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class AnalyticsIntegrationTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "analytics-integration-tests-signing-key-0123456789";

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    private Guid _adminUserId;
    private Guid _learnerUserId;

    public AnalyticsIntegrationTests(ContainersFixture containers)
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
            ["Payment:Stripe:SecretKey"] = "sk_test_placeholder_key_for_testing_purposes_only",
            ["Payment:Stripe:PublishableKey"] = "pk_test_placeholder_key_for_testing_purposes_only",
            ["Payment:Stripe:WebhookSecret"] = "whsec_test_placeholder_webhook_secret_for_tests",
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
                };
            });

        builder.Services.AddSiriAuthorizationPolicies();
        builder.Services.AddPersistence(builder.Configuration);
        builder.Services.AddSharedRedis(builder.Configuration);
        builder.Services.AddIdentityModule(builder.Configuration);
        builder.Services.AddNotificationModule(builder.Configuration);
        builder.Services.AddCatalogModule(builder.Configuration);
        builder.Services.AddCommerceModule(builder.Configuration);
        builder.Services.AddLearningModule();
        builder.Services.AddAnalyticsModule();

        _app = builder.Build();

        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapAnalyticsEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();

        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();

        await SeedDataAsync(scope.ServiceProvider, dbContext);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private async Task SeedDataAsync(IServiceProvider services, AppDbContext dbContext)
    {
        var clock = services.GetRequiredService<IClock>();

        // 1. Admin & Learner
        var admin = await CreateUserAsync(services, dbContext, $"admin_analytics_{Guid.NewGuid():N}@test.com");
        admin.AssignRole(await dbContext.SeededRoleAsync(ROLE.AdminId));
        _adminUserId = admin.Id;

        var learner = await CreateUserAsync(services, dbContext, $"learner_analytics_{Guid.NewGuid():N}@test.com");
        _learnerUserId = learner.Id;

        // 2. Seed Daily COURSE Stat for testing summary
        var stat = DAILY_COURSE_STAT.Create(
            DateOnly.FromDateTime(clock.UtcNow),
            Guid.NewGuid());
        stat.ApplyRollup(250, 10, 12500m, 80m);
        dbContext.DailyCourseStats().Add(stat);

        await dbContext.SaveChangesAsync();
    }

    private static async Task<USER> CreateUserAsync(IServiceProvider services, AppDbContext dbContext, string email)
    {
        var passwordHasher = services.GetRequiredService<IUserPasswordHasher>();
        var clock = services.GetRequiredService<IClock>();

        var normalizedEmail = email.ToUpperInvariant();
        var throwaway = USER.Register(email, normalizedEmail, "placeholder", "Test USER");
        var hash = passwordHasher.HashPassword(throwaway, KnownPassword);
        var user = USER.Register(email, normalizedEmail, hash, "Test USER");
        user.ConfirmEmail(clock);

        dbContext.Users().Add(user);
        await dbContext.SaveChangesAsync();

        return user;
    }

    private async Task<string> LoginAndGetAccessTokenAsync(string email)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var loginHandler = scope.ServiceProvider.GetRequiredService<LoginHandler>();
        var loginResult = await loginHandler.HandleAsync(
            new LoginCommand(email, KnownPassword, "TestDevice", "Test Agent"),
            "UA",
            "127.0.0.1",
            CancellationToken.None);

        if (!loginResult.IsSuccess)
        {
            throw new InvalidOperationException($"Login failed: {loginResult.Error.Code}");
        }

        return loginResult.Value.AccessToken;
    }

    [Fact]
    public async Task GetAdminDashboardSummary_NonAdmin_ReturnsForbidden()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users().FirstAsync(u => u.Id == _learnerUserId);
        var token = await LoginAndGetAccessTokenAsync(user.Email);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/analytics/admin/dashboard/summary");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetAdminDashboardSummary_AdminUser_Returns200WithSummaryData()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users().FirstAsync(u => u.Id == _adminUserId);
        var token = await LoginAndGetAccessTokenAsync(user.Email);

        var request = new HttpRequestMessage(HttpMethod.Get, "/api/analytics/admin/dashboard/summary");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var summary = await response.Content.ReadFromJsonAsync<AdminDashboardSummaryResponse>();
        Assert.NotNull(summary);
        Assert.True(summary.TotalLearners >= 1);
    }

    [Fact]
    public async Task AnalyticsRollupJob_ExecutesWithoutError()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var job = scope.ServiceProvider.GetRequiredService<AnalyticsRollupJob>();

        // Act & Assert (Should complete idempotently without throwing)
        await job.RunAsync(CancellationToken.None);
    }
}
