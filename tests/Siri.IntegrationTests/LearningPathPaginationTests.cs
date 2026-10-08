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
using Siri.Modules.Catalog;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.GetLearningPaths;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Notification;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;
using Xunit;

namespace Siri.IntegrationTests;

/// <summary>
/// Regression coverage for GetLearningPathsHandler's pagination (audit fix, 2026-09-01): it used to return
/// the entire unbounded set of learning paths with no page/pageSize param at all, unlike every other list
/// endpoint in this module (see <see cref="Siri.Modules.Catalog.Features.GetPendingInstructorApplications.GetPendingInstructorApplicationsHandler"/>,
/// whose <see cref="PagedResult{T}"/> pattern this now mirrors).
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class LearningPathPaginationTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "learning-path-pagination-tests-signing-key-0123456789";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public LearningPathPaginationTests(ContainersFixture containers)
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
        builder.Services.AddCatalogModule(builder.Configuration);

        _app = builder.Build();

        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapCatalogEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();

        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
        await SeedOnceAsync(scope.ServiceProvider, dbContext);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task GetPublicLearningPaths_FirstPage_ReturnsPagedResultWithCorrectTotalCount()
    {
        var response = await _client.GetFromJsonAsync<PagedResult<LearningPathSummaryResponse>>(
            "/api/catalog/learning-paths?page=1&pageSize=2", JsonOptions);

        Assert.NotNull(response);
        Assert.Equal(2, response.Items.Count);
        // 3 active paths seeded (2 active out of the total seed set are activeOnly-visible; see SeedDataAsync).
        Assert.Equal(3, response.TotalCount);
        Assert.Equal(1, response.Page);
        Assert.Equal(2, response.PageSize);
        Assert.Equal(2, response.TotalPages);
    }

    [Fact]
    public async Task GetPublicLearningPaths_SecondPage_ReturnsRemainingItem()
    {
        var response = await _client.GetFromJsonAsync<PagedResult<LearningPathSummaryResponse>>(
            "/api/catalog/learning-paths?page=2&pageSize=2", JsonOptions);

        Assert.NotNull(response);
        Assert.Single(response.Items);
        Assert.Equal(3, response.TotalCount);
        Assert.Equal(2, response.Page);
    }

    [Fact]
    public async Task GetPublicLearningPaths_DefaultPaging_OnlyReturnsActivePaths()
    {
        var response = await _client.GetFromJsonAsync<PagedResult<LearningPathSummaryResponse>>(
            "/api/catalog/learning-paths", JsonOptions);

        Assert.NotNull(response);
        Assert.Equal(3, response.TotalCount);
        Assert.All(response.Items, p => Assert.True(p.IsActive));
    }

    [Fact]
    public async Task GetAdminLearningPaths_AsAdmin_IncludesInactivePathsAndIsPaginated()
    {
        var adminToken = await CreateAdminAndLoginAsync();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/catalog/admin/learning-paths?page=1&pageSize=2");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);
        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<PagedResult<LearningPathSummaryResponse>>(JsonOptions);
        Assert.NotNull(body);
        // 3 active + 1 inactive seeded — admin (activeOnly: false) sees all 4 across pages.
        Assert.Equal(4, body.TotalCount);
        Assert.Equal(2, body.Items.Count);
    }

    [Fact]
    public async Task GetAdminLearningPaths_AsAnonymous_Returns401Unauthorized()
    {
        var response = await _client.GetAsync("/api/catalog/admin/learning-paths");
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private async Task<string> CreateAdminAndLoginAsync()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var passwordHasher = scope.ServiceProvider.GetRequiredService<IUserPasswordHasher>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var email = $"admin-{Guid.NewGuid():N}@example.test";
        var normalizedEmail = email.ToUpperInvariant();
        var throwaway = USER.Register(email, normalizedEmail, "placeholder", "Test USER");
        var hash = passwordHasher.HashPassword(throwaway, KnownPassword);
        var user = USER.Register(email, normalizedEmail, hash, "Test USER");
        user.ConfirmEmail(clock);
        user.AssignRole(await dbContext.SeededRoleAsync(ROLE.AdminId));

        dbContext.Users().Add(user);
        await dbContext.SaveChangesAsync();

        var loginHandler = scope.ServiceProvider.GetRequiredService<LoginHandler>();
        var result = await loginHandler.HandleAsync(
            new LoginCommand(email, KnownPassword, "device-1", "Test Device"), "UA", "203.0.113.50", CancellationToken.None);

        Assert.True(result.IsSuccess);
        return result.Value.AccessToken;
    }

    // These tests assert exact TotalCount values over the whole LEARNING_PATHS table (3 active / 4 total), and
    // xUnit re-runs InitializeAsync for every test against the collection's single shared database — so the
    // fixed set must be inserted once per database, not once per test (the slugs are unique-indexed).
    private static readonly SemaphoreSlim SeedLock = new(1, 1);
    private static string? s_seededDatabase;

    private async Task SeedOnceAsync(IServiceProvider services, AppDbContext db)
    {
        await SeedLock.WaitAsync();
        try
        {
            if (s_seededDatabase == _containers.SqlConnectionString)
            {
                return;
            }

            await SeedDataAsync(services, db);
            s_seededDatabase = _containers.SqlConnectionString;
        }
        finally
        {
            SeedLock.Release();
        }
    }

    private static async Task SeedDataAsync(IServiceProvider services, AppDbContext db)
    {
        // 3 active paths (visible on the public endpoint, spread across 2 pages at pageSize=2) + 1
        // inactive path (only visible via the admin endpoint's activeOnly: false).
        db.LearningPaths().Add(LEARNING_PATH.Create("path-alpha", "Path Alpha", null, sortOrder: 1));
        db.LearningPaths().Add(LEARNING_PATH.Create("path-beta", "Path Beta", null, sortOrder: 2));
        db.LearningPaths().Add(LEARNING_PATH.Create("path-gamma", "Path Gamma", null, sortOrder: 3));
        db.LearningPaths().Add(LEARNING_PATH.Create("path-inactive", "Path Inactive", null, sortOrder: 4, isActive: false));

        await db.SaveChangesAsync();
    }
}
