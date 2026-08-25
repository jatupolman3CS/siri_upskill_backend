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
using Siri.Modules.Catalog.Features.CreateCategory;
using Siri.Modules.Catalog.Features.UpdateCategory;
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
/// Real HTTP-level proof of P1-01's category admin CRUD + public tree read — same self-contained
/// <see cref="WebApplication"/> approach <c>DeviceManagementTests</c>/<c>AuthorizationPolicyHttpTests</c>
/// already established (see those classes' own doc comments for why: no path to the real Contabo
/// database can ever open from a test). Requires Docker locally; see <see cref="ContainersFixture"/>'s
/// own doc comment.
/// <para>
/// Users are created directly against <see cref="AppDbContext"/> and logged in via
/// <see cref="LoginHandler"/> resolved straight from the host's own DI container (same pattern
/// <c>DeviceManagementTests</c> uses) rather than over HTTP — this file's actual subject is the six
/// Catalog endpoints, and going through the handler directly gets a real access token without needing
/// Identity's own endpoints mapped at all.
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class CategoryManagementTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "category-management-tests-signing-key-0123456789";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public CategoryManagementTests(ContainersFixture containers)
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
        // AddIdentityModule/AddNotificationModule: not testing Identity's own endpoints here (none are
        // mapped below), but AddIdentityModule registers RegisterHandler/ForgotPasswordHandler, which
        // need IEmailOutbox from Notification — the generic host validates every registered service's
        // dependency graph at Build() time, so this must be present even though this file never calls
        // either handler (same reason DeviceManagementTests includes it).
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
            // directly from the well-known id/name constants, same as Siri.UnitTests.Identity.UserTests.
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

    private async Task<string> CreateAdminAndLoginAsync(IServiceProvider services, AppDbContext dbContext)
    {
        var email = $"admin-{Guid.NewGuid():N}@example.test";
        await CreateUserAsync(services, dbContext, email, KnownPassword, isAdmin: true);
        return await LoginAndGetAccessTokenAsync(services, email);
    }

    private static HttpRequestMessage AuthenticatedRequest(HttpMethod method, string path, string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private async Task<CreateCategoryResponse> CreateCategoryAsync(string adminToken, string slug, Guid? parentId = null)
    {
        using var request = AuthenticatedRequest(HttpMethod.Post, "/api/catalog/admin/categories", adminToken);
        request.Content = JsonContent.Create(new CreateCategoryCommand(slug, $"ชื่อ {slug}", $"Name {slug}", null, parentId));

        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreateCategoryResponse>(JsonOptions);
        Assert.NotNull(body);
        return body!;
    }

    // ---- CreateCategory -----------------------------------------------------------------------

    [Fact]
    public async Task CreateCategory_AsAdmin_AppearsInAdminTreeAsRootCategory()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminToken = await CreateAdminAndLoginAsync(scope.ServiceProvider, dbContext);

        var created = await CreateCategoryAsync(adminToken, $"web-dev-{Guid.NewGuid():N}");

        using var treeResponse = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Get, "/api/catalog/admin/categories", adminToken));
        var tree = await treeResponse.Content.ReadFromJsonAsync<List<CategoryTreeNode>>(JsonOptions);

        Assert.Contains(tree!, n => n.Id == created.Id);
    }

    [Fact]
    public async Task CreateCategory_DuplicateSlug_Returns409Conflict()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminToken = await CreateAdminAndLoginAsync(scope.ServiceProvider, dbContext);
        var slug = $"duplicate-{Guid.NewGuid():N}";
        await CreateCategoryAsync(adminToken, slug);

        using var request = AuthenticatedRequest(HttpMethod.Post, "/api/catalog/admin/categories", adminToken);
        request.Content = JsonContent.Create(new CreateCategoryCommand(slug, "ชื่อซ้ำ", "Duplicate Name", null, null));
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateCategory_AsNonAdmin_Returns403Forbidden()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var email = $"learner-{Guid.NewGuid():N}@example.test";
        await CreateUserAsync(scope.ServiceProvider, dbContext, email, KnownPassword, isAdmin: false);
        var learnerToken = await LoginAndGetAccessTokenAsync(scope.ServiceProvider, email);

        using var request = AuthenticatedRequest(HttpMethod.Post, "/api/catalog/admin/categories", learnerToken);
        request.Content = JsonContent.Create(new CreateCategoryCommand($"forbidden-{Guid.NewGuid():N}", "ชื่อ", "Name", null, null));
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Theory]
    [InlineData("POST", "/api/catalog/admin/categories")]
    [InlineData("GET", "/api/catalog/admin/categories")]
    [InlineData("POST", "/api/catalog/admin/categories/reorder")]
    public async Task AdminCategoryEndpoint_NoAuthorizationHeader_Returns401(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    // ---- GetCategoryTree (public) --------------------------------------------------------------

    [Fact]
    public async Task GetCategoryTree_NoAuthHeader_ReturnsOkWithOnlyActiveCategories()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminToken = await CreateAdminAndLoginAsync(scope.ServiceProvider, dbContext);

        var active = await CreateCategoryAsync(adminToken, $"active-{Guid.NewGuid():N}");
        var toDeactivate = await CreateCategoryAsync(adminToken, $"inactive-{Guid.NewGuid():N}");

        using var deactivateRequest = AuthenticatedRequest(
            HttpMethod.Put, $"/api/catalog/admin/categories/{toDeactivate.Id}", adminToken);
        deactivateRequest.Content = JsonContent.Create(
            new UpdateCategoryCommand(toDeactivate.Slug, toDeactivate.NameTh, toDeactivate.NameEn, null, null, IsActive: false));
        using var deactivateResponse = await _client.SendAsync(deactivateRequest);
        Assert.Equal(HttpStatusCode.OK, deactivateResponse.StatusCode);

        using var publicRequest = new HttpRequestMessage(HttpMethod.Get, "/api/catalog/categories"); // deliberately no auth header
        using var publicResponse = await _client.SendAsync(publicRequest);
        Assert.Equal(HttpStatusCode.OK, publicResponse.StatusCode);

        var tree = await publicResponse.Content.ReadFromJsonAsync<List<CategoryTreeNode>>(JsonOptions);
        Assert.Contains(tree!, n => n.Id == active.Id);
        Assert.DoesNotContain(tree!, n => n.Id == toDeactivate.Id);
    }

    // ---- UpdateCategory -------------------------------------------------------------------------

    [Fact]
    public async Task UpdateCategory_RenameAndChangeSlug_PersistsChanges()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminToken = await CreateAdminAndLoginAsync(scope.ServiceProvider, dbContext);
        var created = await CreateCategoryAsync(adminToken, $"before-{Guid.NewGuid():N}");
        var newSlug = $"after-{Guid.NewGuid():N}";

        using var request = AuthenticatedRequest(HttpMethod.Put, $"/api/catalog/admin/categories/{created.Id}", adminToken);
        request.Content = JsonContent.Create(new UpdateCategoryCommand(newSlug, "ชื่อใหม่", "New Name", "icon-new", null, true));
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UpdateCategoryResponse>(JsonOptions);
        Assert.Equal(newSlug, body!.Slug);
        Assert.Equal("New Name", body.NameEn);
        Assert.Equal("icon-new", body.IconKey);

        var row = await dbContext.Categories().AsNoTracking().SingleAsync(c => c.Id == created.Id);
        Assert.Equal(newSlug, row.Slug);
    }

    /// <summary>The headline test for this task's cycle-prevention design (Handler.cs's own doc
    /// comment) — proves moving a category under its own grandchild is actually rejected over real
    /// HTTP, not just in the handler's unit-level reasoning.</summary>
    [Fact]
    public async Task UpdateCategory_MoveUnderOwnGrandchild_ReturnsValidationProblemAndLeavesTreeUnchanged()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminToken = await CreateAdminAndLoginAsync(scope.ServiceProvider, dbContext);

        var grandparent = await CreateCategoryAsync(adminToken, $"grandparent-{Guid.NewGuid():N}");
        var parent = await CreateCategoryAsync(adminToken, $"parent-{Guid.NewGuid():N}", grandparent.Id);
        var child = await CreateCategoryAsync(adminToken, $"child-{Guid.NewGuid():N}", parent.Id);

        using var request = AuthenticatedRequest(
            HttpMethod.Put, $"/api/catalog/admin/categories/{grandparent.Id}", adminToken);
        request.Content = JsonContent.Create(
            new UpdateCategoryCommand(grandparent.Slug, grandparent.NameTh, grandparent.NameEn, null, child.Id, true));
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var grandparentRow = await dbContext.Categories().AsNoTracking().SingleAsync(c => c.Id == grandparent.Id);
        Assert.Null(grandparentRow.ParentId); // rejected attempt must not mutate the tree
    }

    [Fact]
    public async Task UpdateCategory_MoveToDifferentRootParent_UpdatesParentIdAndAppendsToNewSiblings()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminToken = await CreateAdminAndLoginAsync(scope.ServiceProvider, dbContext);

        var oldParent = await CreateCategoryAsync(adminToken, $"old-parent-{Guid.NewGuid():N}");
        var newParent = await CreateCategoryAsync(adminToken, $"new-parent-{Guid.NewGuid():N}");
        var existingSibling = await CreateCategoryAsync(adminToken, $"existing-sibling-{Guid.NewGuid():N}", newParent.Id);
        var moving = await CreateCategoryAsync(adminToken, $"moving-{Guid.NewGuid():N}", oldParent.Id);

        using var request = AuthenticatedRequest(HttpMethod.Put, $"/api/catalog/admin/categories/{moving.Id}", adminToken);
        request.Content = JsonContent.Create(
            new UpdateCategoryCommand(moving.Slug, moving.NameTh, moving.NameEn, null, newParent.Id, true));
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UpdateCategoryResponse>(JsonOptions);
        Assert.Equal(newParent.Id, body!.ParentId);
        Assert.True(body.SortOrder > existingSibling.SortOrder); // appended to the end, not colliding with the existing sibling
    }

    // ---- ReorderCategories ----------------------------------------------------------------------

    [Fact]
    public async Task ReorderCategories_FullSiblingSet_UpdatesSortOrder()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminToken = await CreateAdminAndLoginAsync(scope.ServiceProvider, dbContext);

        var parent = await CreateCategoryAsync(adminToken, $"parent-{Guid.NewGuid():N}");
        var first = await CreateCategoryAsync(adminToken, $"first-{Guid.NewGuid():N}", parent.Id);
        var second = await CreateCategoryAsync(adminToken, $"second-{Guid.NewGuid():N}", parent.Id);

        using var request = AuthenticatedRequest(HttpMethod.Post, "/api/catalog/admin/categories/reorder", adminToken);
        request.Content = JsonContent.Create(new
        {
            items = new[]
            {
                new { categoryId = first.Id, sortOrder = 1 },
                new { categoryId = second.Id, sortOrder = 0 },
            },
        });
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var firstRow = await dbContext.Categories().AsNoTracking().SingleAsync(c => c.Id == first.Id);
        var secondRow = await dbContext.Categories().AsNoTracking().SingleAsync(c => c.Id == second.Id);
        Assert.Equal(1, firstRow.SortOrder);
        Assert.Equal(0, secondRow.SortOrder);
    }

    [Fact]
    public async Task ReorderCategories_PartialSiblingSet_ReturnsValidationProblemAndLeavesSortOrderUnchanged()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminToken = await CreateAdminAndLoginAsync(scope.ServiceProvider, dbContext);

        var parent = await CreateCategoryAsync(adminToken, $"parent-{Guid.NewGuid():N}");
        var first = await CreateCategoryAsync(adminToken, $"first-{Guid.NewGuid():N}", parent.Id);
        await CreateCategoryAsync(adminToken, $"second-{Guid.NewGuid():N}", parent.Id); // deliberately omitted from the batch below

        using var request = AuthenticatedRequest(HttpMethod.Post, "/api/catalog/admin/categories/reorder", adminToken);
        request.Content = JsonContent.Create(new { items = new[] { new { categoryId = first.Id, sortOrder = 5 } } });
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        var firstRow = await dbContext.Categories().AsNoTracking().SingleAsync(c => c.Id == first.Id);
        Assert.Equal(first.SortOrder, firstRow.SortOrder); // rejected batch must not partially apply
    }

    // ---- DeleteCategory -------------------------------------------------------------------------

    [Fact]
    public async Task DeleteCategory_HasChildren_Returns409ConflictAndLeavesItInPlace()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminToken = await CreateAdminAndLoginAsync(scope.ServiceProvider, dbContext);

        var parent = await CreateCategoryAsync(adminToken, $"parent-{Guid.NewGuid():N}");
        await CreateCategoryAsync(adminToken, $"child-{Guid.NewGuid():N}", parent.Id);

        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Delete, $"/api/catalog/admin/categories/{parent.Id}", adminToken));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(await dbContext.Categories().AsNoTracking().AnyAsync(c => c.Id == parent.Id));
    }

    [Fact]
    public async Task DeleteCategory_NoChildren_SucceedsAndRemovesRow()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var adminToken = await CreateAdminAndLoginAsync(scope.ServiceProvider, dbContext);

        var created = await CreateCategoryAsync(adminToken, $"deletable-{Guid.NewGuid():N}");

        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Delete, $"/api/catalog/admin/categories/{created.Id}", adminToken));

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
        Assert.False(await dbContext.Categories().AsNoTracking().AnyAsync(c => c.Id == created.Id));
    }
}
