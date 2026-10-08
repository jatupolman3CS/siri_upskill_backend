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
using Siri.Modules.Catalog.Features.CreateCourse;
using Siri.Modules.Catalog.Features.GetCourse;
using Siri.Modules.Catalog.Features.GetMyCourses;
using Siri.Modules.Catalog.Features.UpdateCourse;
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
/// Real HTTP-level proof of P1-04's instructor-facing course CRUD — same self-contained
/// <see cref="WebApplication"/> approach <see cref="CategoryManagementTests"/>/
/// <see cref="InstructorApplicationTests"/> already established (see those classes' own doc comments for
/// why). Requires Docker locally; see <see cref="ContainersFixture"/>'s own doc comment.
/// <para>
/// Fixture setup (an approved <see cref="INSTRUCTOR_PROFILE"/>, a <see cref="CATEGORY"/>) is built directly
/// against <see cref="AppDbContext"/> rather than through P1-01's/P1-03's own HTTP endpoints — this
/// file's actual subject is the five COURSE endpoints, and those flows already have their own dedicated
/// integration tests (<see cref="CategoryManagementTests"/>, <see cref="InstructorApplicationTests"/>);
/// re-driving them here through HTTP would only slow this file down without adding coverage.
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class CourseManagementTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "course-management-tests-signing-key-0123456789";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public CourseManagementTests(ContainersFixture containers)
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
        builder.Services.AddScoped<Siri.SharedKernel.Contracts.IMediaAssetContract, Siri.Modules.Media.Application.MediaAssetContractService>();

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

    // ---- Fixture setup (direct-domain, not HTTP — see class doc comment) ------------------------

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

    /// <summary>Creates a user, grants them the Instructor role directly (same
    /// <c>user.AssignRole(new ROLE(ROLE.InstructorId, ROLE.InstructorName))</c> pattern
    /// <see cref="CategoryManagementTests"/> uses for its Admin fixture), and gives them an Approved
    /// <see cref="INSTRUCTOR_PROFILE"/> — everything <see cref="CreateCourseHandler"/> requires to let
    /// them create a course, built directly rather than through P1-03's real apply/approve HTTP flow (see
    /// class doc comment).</summary>
    private async Task<(INSTRUCTOR_PROFILE Profile, string AccessToken)> CreateApprovedInstructorAndLoginAsync(
        IServiceProvider services, AppDbContext dbContext)
    {
        var clock = services.GetRequiredService<IClock>();
        var email = $"instructor-{Guid.NewGuid():N}@example.test";
        var user = await CreateUserAsync(services, dbContext, email, KnownPassword);

        user.AssignRole(await dbContext.SeededRoleAsync(ROLE.InstructorId));
        var profile = INSTRUCTOR_PROFILE.Apply(user.Id, "Test Instructor", "Headline", "Bio");
        profile.Approve(clock);
        dbContext.InstructorProfiles().Add(profile);
        await dbContext.SaveChangesAsync();

        var token = await LoginAndGetAccessTokenAsync(services, email);
        return (profile, token);
    }

    private async Task<string> CreatePlainUserAndLoginAsync(IServiceProvider services, AppDbContext dbContext)
    {
        var email = $"learner-{Guid.NewGuid():N}@example.test";
        await CreateUserAsync(services, dbContext, email, KnownPassword);
        return await LoginAndGetAccessTokenAsync(services, email);
    }

    private static async Task<CATEGORY> CreateCategoryAsync(AppDbContext dbContext)
    {
        var category = CATEGORY.Create($"category-{Guid.NewGuid():N}", "หมวดหมู่ทดสอบ", "Test CATEGORY", null, null, 0);
        dbContext.Categories().Add(category);
        await dbContext.SaveChangesAsync();
        return category;
    }

    /// <summary>Fabricates a Published course (section + episode with media, then Publish) purely to test
    /// UpdateCourse/DeleteCourse's Draft-only gate — P1-04 has no endpoint that can reach a non-Draft
    /// status itself (Publish workflow is P1-05's), so this drives the domain directly the same way
    /// Siri.UnitTests.Catalog.CourseTests already proves <c>COURSE.Publish</c> works.</summary>
    private static async Task<COURSE> CreatePublishedCourseAsync(
        IServiceProvider services, AppDbContext dbContext, Guid instructorProfileId, Guid categoryId)
    {
        var clock = services.GetRequiredService<IClock>();
        var course = COURSE.Create($"published-{Guid.NewGuid():N}", "Published COURSE", instructorProfileId, categoryId, CourseLevel.Beginner, CourseLanguage.Thai, 990m);
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);
        course.Publish(clock);

        dbContext.Courses().Add(course);
        await dbContext.SaveChangesAsync();
        return course;
    }

    private static HttpRequestMessage AuthenticatedRequest(HttpMethod method, string path, string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private async Task<CreateCourseResponse> CreateCourseAsync(string instructorToken, Guid categoryId, string? title = null)
    {
        using var request = AuthenticatedRequest(HttpMethod.Post, "/api/catalog/instructor/courses", instructorToken);
        request.Content = JsonContent.Create(new CreateCourseCommand(
            title ?? $"Web Development {Guid.NewGuid():N}", categoryId, CourseLevel.Beginner, CourseLanguage.Thai, 990m));

        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreateCourseResponse>(JsonOptions);
        Assert.NotNull(body);
        return body!;
    }

    // ---- CreateCourse ---------------------------------------------------------------------------

    [Fact]
    public async Task CreateCourse_AsApprovedInstructor_CreatesDraftCourseWithGeneratedSlug()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);

        var response = await CreateCourseAsync(instructorToken, category.Id, "Web Development");

        Assert.Equal(CourseStatus.Draft, response.Status);
        Assert.False(string.IsNullOrWhiteSpace(response.Slug));
        Assert.StartsWith("web-development", response.Slug);
    }

    [Fact]
    public async Task CreateCourse_TitleWithThaiCharacters_GeneratesTransliteratedAsciiSlug()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);

        var response = await CreateCourseAsync(instructorToken, category.Id, "พัฒนาเว็บไซต์เบื้องต้น");

        Assert.Matches("^[a-z0-9]+(-[a-z0-9]+)*$", response.Slug);
    }

    [Fact]
    public async Task CreateCourse_DuplicateTitle_SecondCourseGetsNumericSuffixSlug()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var uniqueTitle = $"Duplicate Title {Guid.NewGuid():N}";

        var first = await CreateCourseAsync(instructorToken, category.Id, uniqueTitle);
        var second = await CreateCourseAsync(instructorToken, category.Id, uniqueTitle);

        Assert.Equal($"{first.Slug}-2", second.Slug);
    }

    [Fact]
    public async Task CreateCourse_WithoutApprovedInstructorProfile_Returns403Forbidden()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        // Admin role passes the group-level InstructorOnly policy but has no INSTRUCTOR_PROFILE of their
        // own — proves CreateCourseHandler's own "must be Approved" check independently defends the rule
        // (defense in depth), not just the role-claim gate.
        var email = $"admin-{Guid.NewGuid():N}@example.test";
        var adminUser = await CreateUserAsync(scope.ServiceProvider, dbContext, email, KnownPassword);
        adminUser.AssignRole(await dbContext.SeededRoleAsync(ROLE.AdminId));
        await dbContext.SaveChangesAsync();
        var adminToken = await LoginAndGetAccessTokenAsync(scope.ServiceProvider, email);
        var category = await CreateCategoryAsync(dbContext);

        using var request = AuthenticatedRequest(HttpMethod.Post, "/api/catalog/instructor/courses", adminToken);
        request.Content = JsonContent.Create(new CreateCourseCommand("Some COURSE", category.Id, CourseLevel.Beginner, CourseLanguage.Thai, 990m));
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateCourse_UnknownCategoryId_Returns404NotFound()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);

        using var request = AuthenticatedRequest(HttpMethod.Post, "/api/catalog/instructor/courses", instructorToken);
        request.Content = JsonContent.Create(new CreateCourseCommand("Some COURSE", Guid.NewGuid(), CourseLevel.Beginner, CourseLanguage.Thai, 990m));
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task CreateCourse_CallerWithNoElevatedRole_Returns403ForbiddenFromGroupPolicy()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var plainToken = await CreatePlainUserAndLoginAsync(scope.ServiceProvider, dbContext);

        using var request = AuthenticatedRequest(HttpMethod.Post, "/api/catalog/instructor/courses", plainToken);
        request.Content = JsonContent.Create(new CreateCourseCommand("Some COURSE", Guid.NewGuid(), CourseLevel.Beginner, CourseLanguage.Thai, 990m));
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- GetMyCourses / GetCourse -----------------------------------------------------------------

    [Fact]
    public async Task GetMyCourses_TwoDifferentInstructors_EachSeesOnlyTheirOwnCourses()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, tokenA) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var (_, tokenB) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var courseA = await CreateCourseAsync(tokenA, category.Id);
        var courseB = await CreateCourseAsync(tokenB, category.Id);

        using var responseA = await _client.SendAsync(AuthenticatedRequest(HttpMethod.Get, "/api/catalog/instructor/courses", tokenA));
        var pageA = await responseA.Content.ReadFromJsonAsync<PagedResult<CourseSummary>>(JsonOptions);

        Assert.Contains(pageA!.Items, c => c.Id == courseA.Id);
        Assert.DoesNotContain(pageA.Items, c => c.Id == courseB.Id);
    }

    [Fact]
    public async Task GetCourse_OwnCourse_ReturnsFullDetail()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var created = await CreateCourseAsync(instructorToken, category.Id, "Detail COURSE");

        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Get, $"/api/catalog/instructor/courses/{created.Id}", instructorToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CourseResponse>(JsonOptions);
        Assert.Equal("Detail COURSE", body!.Title);
    }

    [Fact]
    public async Task GetCourse_AnotherInstructorsCourse_Returns403Forbidden()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, ownerToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var (_, otherToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var created = await CreateCourseAsync(ownerToken, category.Id);

        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Get, $"/api/catalog/instructor/courses/{created.Id}", otherToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task GetCourse_UnknownId_Returns404NotFound()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);

        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Get, $"/api/catalog/instructor/courses/{Guid.NewGuid()}", instructorToken));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- UpdateCourse ---------------------------------------------------------------------------

    [Fact]
    public async Task UpdateCourse_OwnDraftCourse_PersistsChanges()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var created = await CreateCourseAsync(instructorToken, category.Id);

        using var request = AuthenticatedRequest(HttpMethod.Put, $"/api/catalog/instructor/courses/{created.Id}", instructorToken);
        request.Content = JsonContent.Create(new UpdateCourseCommand(
            "Updated Title", "Updated Subtitle", "Updated Description", category.Id,
            CourseLevel.Advanced, CourseLanguage.English, null, 1990m, 2990m, 180, "SEO Title", "SEO Description"));
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<UpdateCourseResponse>(JsonOptions);
        Assert.Equal("Updated Title", body!.Title);
        Assert.Equal(1990m, body.Price);
        Assert.Equal(CourseLevel.Advanced, body.Level);

        var row = await dbContext.Courses().AsNoTracking().SingleAsync(c => c.Id == created.Id);
        Assert.Equal("Updated Title", row.Title);
        Assert.Equal(created.Slug, row.Slug); // slug stays stable — not editable in this task's scope
    }

    [Fact]
    public async Task UpdateCourse_AnotherInstructorsCourse_Returns403ForbiddenAndLeavesUnchanged()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, ownerToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var (_, otherToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var created = await CreateCourseAsync(ownerToken, category.Id, "Original Title");

        using var request = AuthenticatedRequest(HttpMethod.Put, $"/api/catalog/instructor/courses/{created.Id}", otherToken);
        request.Content = JsonContent.Create(new UpdateCourseCommand(
            "Hijacked Title", null, null, category.Id, CourseLevel.Beginner, CourseLanguage.Thai, null, 1m, null, null, null, null));
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);

        var row = await dbContext.Courses().AsNoTracking().SingleAsync(c => c.Id == created.Id);
        Assert.Equal("Original Title", row.Title);
    }

    [Fact]
    public async Task UpdateCourse_NonDraftCourse_Returns409Conflict()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (profile, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var published = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, profile.Id, category.Id);

        using var request = AuthenticatedRequest(HttpMethod.Put, $"/api/catalog/instructor/courses/{published.Id}", instructorToken);
        request.Content = JsonContent.Create(new UpdateCourseCommand(
            "New Title", null, null, category.Id, CourseLevel.Beginner, CourseLanguage.Thai, null, 1m, null, null, null, null));
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    // ---- DeleteCourse ---------------------------------------------------------------------------

    [Fact]
    public async Task DeleteCourse_OwnDraftCourse_SucceedsAndSoftDeletes()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var created = await CreateCourseAsync(instructorToken, category.Id);

        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Delete, $"/api/catalog/instructor/courses/{created.Id}", instructorToken));
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // The global soft-delete query filter hides it from the normal DbSet — IgnoreQueryFilters proves
        // the row still physically exists with IsDeleted = true, not actually gone (database.md: "ห้าม
        // Hard delete ... ใช้สถานะ").
        var row = await dbContext.Courses().IgnoreQueryFilters().AsNoTracking().SingleAsync(c => c.Id == created.Id);
        Assert.True(row.IsDeleted);
        Assert.NotNull(row.DeletedAtUtc);

        var visibleAfterDelete = await dbContext.Courses().AsNoTracking().AnyAsync(c => c.Id == created.Id);
        Assert.False(visibleAfterDelete);
    }

    [Fact]
    public async Task DeleteCourse_AnotherInstructorsCourse_Returns403ForbiddenAndLeavesItInPlace()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, ownerToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var (_, otherToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var created = await CreateCourseAsync(ownerToken, category.Id);

        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Delete, $"/api/catalog/instructor/courses/{created.Id}", otherToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        Assert.True(await dbContext.Courses().AsNoTracking().AnyAsync(c => c.Id == created.Id));
    }

    [Fact]
    public async Task DeleteCourse_NonDraftCourse_Returns409ConflictAndLeavesItInPlace()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (profile, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var published = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, profile.Id, category.Id);

        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Delete, $"/api/catalog/instructor/courses/{published.Id}", instructorToken));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        Assert.True(await dbContext.Courses().AsNoTracking().AnyAsync(c => c.Id == published.Id));
    }

    // ---- Group-level authorization ----------------------------------------------------------------

    [Theory]
    [InlineData("GET", "/api/catalog/instructor/courses")]
    [InlineData("POST", "/api/catalog/instructor/courses")]
    public async Task InstructorCourseEndpoint_NoAuthorizationHeader_Returns401(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
