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
using Siri.Modules.Catalog.Features.ApplyAsInstructor;
using Siri.Modules.Catalog.Features.ApproveInstructorApplication;
using Siri.Modules.Catalog.Features.GetMyInstructorProfile;
using Siri.Modules.Catalog.Features.GetPendingInstructorApplications;
using Siri.Modules.Catalog.Features.RejectInstructorApplication;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Notification;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// Real HTTP-level proof of P1-03's instructor application flow — same self-contained
/// <see cref="WebApplication"/> approach <see cref="CategoryManagementTests"/>/<c>DeviceManagementTests</c>
/// already established (see those classes' own doc comments for why: no path to the real Contabo
/// database can ever open from a test). Requires Docker locally; see <see cref="ContainersFixture"/>'s
/// own doc comment.
/// <para>
/// The headline scenario this file exists to prove end-to-end, not just at the handler-unit level:
/// approving an application actually grants the <c>Instructor</c> role through
/// <c>Siri.Modules.Identity.Contracts.IInstructorRoleGrantor</c> — this codebase's first real
/// cross-module <c>Contracts</c> call — atomically with <see cref="InstructorProfile"/>'s own status
/// change. <c>AddIdentityModule</c>/<c>AddNotificationModule</c> are both registered (same reasoning
/// <see cref="CategoryManagementTests"/> gives) even though this file never maps or calls Identity's own
/// HTTP endpoints directly.
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class InstructorApplicationTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "instructor-application-tests-signing-key-0123456789";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public InstructorApplicationTests(ContainersFixture containers)
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
            ["Email:Provider"] = "Log", // never a real SMTP send
        });

        // Mirrors Program.cs's own AddAuthentication/AddJwtBearer wiring.
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

        _app = builder.Build();

        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapCatalogEndpoints();

        await _app.StartAsync();
        _client = _app.GetTestClient();

        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    private static async Task<User> CreateUserAsync(
        IServiceProvider services, AppDbContext dbContext, string email, string password, bool isAdmin)
    {
        var passwordHasher = services.GetRequiredService<IUserPasswordHasher>();
        var clock = services.GetRequiredService<IClock>();

        var normalizedEmail = email.ToUpperInvariant();
        var throwaway = User.Register(email, normalizedEmail, "placeholder", "Test User");
        var hash = passwordHasher.HashPassword(throwaway, password);
        var user = User.Register(email, normalizedEmail, hash, "Test User");
        user.ConfirmEmail(clock);

        if (isAdmin)
        {
            // Roles are migration-seeded fixed reference data (RoleConfiguration.HasData) — constructed
            // directly from the well-known id/name constants, same as CategoryManagementTests.
            user.AssignRole(new Role(Role.AdminId, Role.AdminName));
        }

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

    private async Task<(User User, string AccessToken)> CreateUserAndLoginAsync(
        IServiceProvider services, AppDbContext dbContext, bool isAdmin = false)
    {
        var email = $"user-{Guid.NewGuid():N}@example.test";
        var user = await CreateUserAsync(services, dbContext, email, KnownPassword, isAdmin);
        var token = await LoginAndGetAccessTokenAsync(services, email);
        return (user, token);
    }

    private static HttpRequestMessage AuthenticatedRequest(HttpMethod method, string path, string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private async Task<ApplyAsInstructorResponse> ApplyAsInstructorAsync(string accessToken, string? displayName = null)
    {
        using var request = AuthenticatedRequest(HttpMethod.Post, "/api/catalog/instructors/apply", accessToken);
        request.Content = JsonContent.Create(new ApplyAsInstructorCommand(
            displayName ?? "Somchai Dev", "Senior Full-Stack Developer", "สอนพัฒนาเว็บมา 10 ปี"));

        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApplyAsInstructorResponse>(JsonOptions);
        Assert.NotNull(body);
        return body!;
    }

    // ---- ApplyAsInstructor ----------------------------------------------------------------------

    [Fact]
    public async Task ApplyAsInstructor_AsAuthenticatedUser_CreatesPendingApplicationWithDefaultRevenueShare()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, token) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);

        var response = await ApplyAsInstructorAsync(token);

        Assert.Equal(InstructorApplicationStatus.Pending, response.Status);
        Assert.Equal(InstructorProfile.DefaultRevenueSharePercent, response.RevenueSharePercent);
        Assert.Null(response.ApprovedAtUtc);
    }

    [Fact]
    public async Task ApplyAsInstructor_NoAuthorizationHeader_Returns401()
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/catalog/instructors/apply")
        {
            Content = JsonContent.Create(new ApplyAsInstructorCommand("Somchai Dev", null, "Bio")),
        };

        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task ApplyAsInstructor_AlreadyPending_Returns409ConflictAndLeavesOriginalUnchanged()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (user, token) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);
        await ApplyAsInstructorAsync(token, "First Attempt");

        using var request = AuthenticatedRequest(HttpMethod.Post, "/api/catalog/instructors/apply", token);
        request.Content = JsonContent.Create(new ApplyAsInstructorCommand("Second Attempt", null, "Bio"));
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var row = await dbContext.InstructorProfiles().AsNoTracking().SingleAsync(p => p.UserId == user.Id);
        Assert.Equal("First Attempt", row.DisplayName); // rejected second attempt must not overwrite the pending one
    }

    [Fact]
    public async Task ApplyAsInstructor_AfterRejection_ResubmitsSameRowAndReturnsToPending()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (user, applicantToken) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);
        var (_, adminToken) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext, isAdmin: true);
        var firstApplication = await ApplyAsInstructorAsync(applicantToken, "Before Rejection");
        await _client.SendAsync(AuthenticatedRequest(
            HttpMethod.Post, $"/api/catalog/admin/instructors/{firstApplication.Id}/reject", adminToken));

        var resubmitted = await ApplyAsInstructorAsync(applicantToken, "After Rejection");

        Assert.Equal(firstApplication.Id, resubmitted.Id); // same row reused, not a second insert
        Assert.Equal(InstructorApplicationStatus.Pending, resubmitted.Status);
        Assert.Equal("After Rejection", resubmitted.DisplayName);

        var count = await dbContext.InstructorProfiles().AsNoTracking().CountAsync(p => p.UserId == user.Id);
        Assert.Equal(1, count); // UserId's unique index — never a second row
    }

    // ---- GetMyInstructorProfile -------------------------------------------------------------------

    [Fact]
    public async Task GetMyInstructorProfile_NeverApplied_Returns404()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, token) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);

        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Get, "/api/catalog/instructors/me", token));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetMyInstructorProfile_TwoDifferentApplicants_EachSeesOnlyTheirOwnProfile()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, tokenA) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);
        var (_, tokenB) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);
        var applicationA = await ApplyAsInstructorAsync(tokenA, "Applicant A");
        var applicationB = await ApplyAsInstructorAsync(tokenB, "Applicant B");

        using var responseA = await _client.SendAsync(AuthenticatedRequest(HttpMethod.Get, "/api/catalog/instructors/me", tokenA));
        using var responseB = await _client.SendAsync(AuthenticatedRequest(HttpMethod.Get, "/api/catalog/instructors/me", tokenB));

        var bodyA = await responseA.Content.ReadFromJsonAsync<InstructorProfileResponse>(JsonOptions);
        var bodyB = await responseB.Content.ReadFromJsonAsync<InstructorProfileResponse>(JsonOptions);

        Assert.Equal(applicationA.Id, bodyA!.Id);
        Assert.Equal("Applicant A", bodyA.DisplayName);
        Assert.Equal(applicationB.Id, bodyB!.Id);
        Assert.Equal("Applicant B", bodyB.DisplayName);
    }

    // ---- GetPendingInstructorApplications (admin) --------------------------------------------------

    [Fact]
    public async Task GetPendingInstructorApplications_AsAdmin_ListsThePendingApplication()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, applicantToken) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);
        var (_, adminToken) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext, isAdmin: true);
        var application = await ApplyAsInstructorAsync(applicantToken);

        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Get, "/api/catalog/admin/instructors/pending", adminToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<PagedResult<InstructorApplicationSummary>>(JsonOptions);
        Assert.Contains(page!.Items, i => i.Id == application.Id);
    }

    [Fact]
    public async Task GetPendingInstructorApplications_AsNonAdmin_Returns403Forbidden()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, token) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);

        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Get, "/api/catalog/admin/instructors/pending", token));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- ApproveInstructorApplication (admin) -------------------------------------------------------

    /// <summary>The headline test for this task's cross-module design (Handler.cs's own doc comment) —
    /// proves approving over real HTTP actually grants the Instructor role through Identity's
    /// IInstructorRoleGrantor contract, not just that InstructorProfile's own status flips.</summary>
    [Fact]
    public async Task ApproveInstructorApplication_AsAdmin_SetsApprovedStatusAndGrantsInstructorRoleToUser()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (applicant, applicantToken) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);
        var (_, adminToken) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext, isAdmin: true);
        var application = await ApplyAsInstructorAsync(applicantToken);

        using var response = await _client.SendAsync(AuthenticatedRequest(
            HttpMethod.Post, $"/api/catalog/admin/instructors/{application.Id}/approve", adminToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApproveInstructorApplicationResponse>(JsonOptions);
        Assert.Equal(InstructorApplicationStatus.Approved, body!.Status);
        Assert.NotNull(body.ApprovedAtUtc);

        var hasInstructorRole = await dbContext.Users()
            .AsNoTracking()
            .Where(u => u.Id == applicant.Id)
            .SelectMany(u => u.Roles)
            .AnyAsync(r => r.Name == Role.InstructorName);
        Assert.True(hasInstructorRole);
    }

    [Fact]
    public async Task ApproveInstructorApplication_AlreadyApproved_Returns409Conflict()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, applicantToken) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);
        var (_, adminToken) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext, isAdmin: true);
        var application = await ApplyAsInstructorAsync(applicantToken);
        await _client.SendAsync(AuthenticatedRequest(
            HttpMethod.Post, $"/api/catalog/admin/instructors/{application.Id}/approve", adminToken));

        using var response = await _client.SendAsync(AuthenticatedRequest(
            HttpMethod.Post, $"/api/catalog/admin/instructors/{application.Id}/approve", adminToken));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ApproveInstructorApplication_UnknownId_Returns404NotFound()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, adminToken) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext, isAdmin: true);

        using var response = await _client.SendAsync(AuthenticatedRequest(
            HttpMethod.Post, $"/api/catalog/admin/instructors/{Guid.NewGuid()}/approve", adminToken));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task ApproveInstructorApplication_AsNonAdmin_Returns403Forbidden()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, applicantToken) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);
        var application = await ApplyAsInstructorAsync(applicantToken);

        using var response = await _client.SendAsync(AuthenticatedRequest(
            HttpMethod.Post, $"/api/catalog/admin/instructors/{application.Id}/approve", applicantToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- RejectInstructorApplication (admin) --------------------------------------------------------

    [Fact]
    public async Task RejectInstructorApplication_AsAdmin_SetsRejectedStatusAndDoesNotGrantInstructorRole()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (applicant, applicantToken) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);
        var (_, adminToken) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext, isAdmin: true);
        var application = await ApplyAsInstructorAsync(applicantToken);

        using var response = await _client.SendAsync(AuthenticatedRequest(
            HttpMethod.Post, $"/api/catalog/admin/instructors/{application.Id}/reject", adminToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<RejectInstructorApplicationResponse>(JsonOptions);
        Assert.Equal(InstructorApplicationStatus.Rejected, body!.Status);

        var hasInstructorRole = await dbContext.Users()
            .AsNoTracking()
            .Where(u => u.Id == applicant.Id)
            .SelectMany(u => u.Roles)
            .AnyAsync(r => r.Name == Role.InstructorName);
        Assert.False(hasInstructorRole);
    }

    [Theory]
    [InlineData("/api/catalog/admin/instructors/pending")]
    public async Task AdminInstructorEndpoint_NoAuthorizationHeader_Returns401(string path)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, path);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
