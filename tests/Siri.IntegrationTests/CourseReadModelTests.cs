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
using Siri.Modules.Catalog.Features.GetCourseDetail;
using Siri.Modules.Catalog.Features.SearchCourses;
using Siri.Modules.Catalog.Features.UnpublishCourse;
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
/// Real HTTP-level proof of P1-07's public course detail projection and output-cache invalidation — same
/// self-contained <see cref="WebApplication"/> approach every other Catalog integration test file already
/// establishes. Requires Docker locally; see <see cref="ContainersFixture"/>'s own doc comment.
/// <para>
/// <see cref="SearchThenApprove_NewlyPublishedCourseAppearsInSearchDespiteEarlierCachedResult"/> is a
/// deliberately black-box proof of cache invalidation: rather than inspecting
/// <c>IOutputCacheStore</c>/HTTP cache headers directly (fragile, and this codebase's output cache is the
/// default in-memory store with no inspectable-from-outside state), it drives the actual observable
/// behavior — search once before a course is published (populating a cached "not found" result for that
/// exact query), approve the course, search again with the identical query, and assert the course now
/// appears. If <c>ApproveCourseHandler</c>'s <c>EvictByTagAsync</c> call were missing or wrong, this
/// specific test would still see the first call's stale cached result and fail.
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class CourseReadModelTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "course-read-model-tests-signing-key-0123456789";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public CourseReadModelTests(ContainersFixture containers)
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
        _app.UseOutputCache();

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

    // ---- Fixture setup (direct-domain, not HTTP — see other Catalog test files' own doc comments) ----

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

    private static async Task<string> LoginAndGetAccessTokenAsync(IServiceProvider services, string email)
    {
        var loginHandler = services.GetRequiredService<LoginHandler>();
        var result = await loginHandler.HandleAsync(
            new LoginCommand(email, KnownPassword, "device-1", "Test Device"), "UA", "203.0.113.50", CancellationToken.None);

        Assert.True(result.IsSuccess);
        return result.Value.AccessToken;
    }

    private static async Task<INSTRUCTOR_PROFILE> CreateApprovedInstructorAsync(IServiceProvider services, AppDbContext dbContext)
    {
        var clock = services.GetRequiredService<IClock>();
        var profile = INSTRUCTOR_PROFILE.Apply(Guid.NewGuid(), "Test Instructor", "Test Headline", "Test Bio");
        profile.Approve(clock);
        dbContext.InstructorProfiles().Add(profile);
        await dbContext.SaveChangesAsync();
        return profile;
    }

    private static async Task<string> CreateAdminAndLoginAsync(IServiceProvider services, AppDbContext dbContext)
    {
        var email = $"admin-{Guid.NewGuid():N}@example.test";
        var user = await CreateUserAsync(services, dbContext, email);
        user.AssignRole(new ROLE(ROLE.AdminId, ROLE.AdminName));
        await dbContext.SaveChangesAsync();
        return await LoginAndGetAccessTokenAsync(services, email);
    }

    private static async Task<CATEGORY> CreateCategoryAsync(AppDbContext dbContext)
    {
        var category = CATEGORY.Create($"category-{Guid.NewGuid():N}", "หมวดหมู่ทดสอบ", "Test CATEGORY", null, null, 0);
        dbContext.Categories().Add(category);
        await dbContext.SaveChangesAsync();
        return category;
    }

    private static async Task<COURSE> CreatePublishedCourseAsync(
        IServiceProvider services, AppDbContext dbContext, Guid instructorId, Guid categoryId, string title)
    {
        var clock = services.GetRequiredService<IClock>();
        var course = COURSE.Create($"course-{Guid.NewGuid():N}", title, instructorId, categoryId, CourseLevel.Beginner, CourseLanguage.Thai, 990m);
        var section = course.AddSection("Section 1");
        section.AddEpisode("Free Intro", null, isFreePreview: true).AttachMedia(Guid.NewGuid(), 300);
        section.AddEpisode("Deep Dive", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 900);
        course.AddOutcome("เข้าใจพื้นฐาน");
        course.AddRequirement("มีคอมพิวเตอร์");
        course.Publish(clock);

        dbContext.Courses().Add(course);
        await dbContext.SaveChangesAsync();
        return course;
    }

    /// <summary>Builds a course, submits it for review, but stops short of publishing — used by the
    /// cache-invalidation test, which needs to approve it itself mid-test.</summary>
    private static async Task<COURSE> CreateInReviewCourseAsync(
        IServiceProvider services, AppDbContext dbContext, Guid instructorId, Guid categoryId, string title)
    {
        var course = COURSE.Create($"course-{Guid.NewGuid():N}", title, instructorId, categoryId, CourseLevel.Beginner, CourseLanguage.Thai, 990m);
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);
        course.SubmitForReview();

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

    // ---- GetCourseDetail ----------------------------------------------------------------------------

    [Fact]
    public async Task GetCourseDetail_PublishedCourse_ReturnsFullDetailWithSyllabusOutcomesAndInstructor()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var instructor = await CreateApprovedInstructorAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var course = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, instructor.Id, category.Id, "Detail Test COURSE");

        using var response = await _client.GetAsync($"/api/catalog/courses/{course.Slug}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CourseDetailResponse>(JsonOptions);
        Assert.Equal(course.Id, body!.Id);
        Assert.Equal(instructor.DisplayName, body.Instructor.DisplayName);
        Assert.Single(body.Outcomes);
        Assert.Single(body.Requirements);
        Assert.Single(body.Sections);
        Assert.Equal(2, body.Sections[0].Episodes.Count);
        Assert.True(body.Sections[0].Episodes[0].IsFreePreview);
        Assert.False(body.Sections[0].Episodes[1].IsFreePreview);
    }

    [Fact]
    public async Task GetCourseDetail_DraftCourse_Returns404NotFound()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var instructor = await CreateApprovedInstructorAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var draft = COURSE.Create($"course-{Guid.NewGuid():N}", "Draft COURSE", instructor.Id, category.Id, CourseLevel.Beginner, CourseLanguage.Thai, 990m);
        dbContext.Courses().Add(draft);
        await dbContext.SaveChangesAsync();

        using var response = await _client.GetAsync($"/api/catalog/courses/{draft.Slug}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetCourseDetail_UnknownSlug_Returns404NotFound()
    {
        using var response = await _client.GetAsync($"/api/catalog/courses/does-not-exist-{Guid.NewGuid():N}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    // ---- Output cache invalidation -------------------------------------------------------------------

    [Fact]
    public async Task SearchThenApprove_NewlyPublishedCourseAppearsInSearchDespiteEarlierCachedResult()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var instructor = await CreateApprovedInstructorAsync(scope.ServiceProvider, dbContext);
        var adminToken = await CreateAdminAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var course = await CreateInReviewCourseAsync(scope.ServiceProvider, dbContext, instructor.Id, category.Id, "Cache Invalidation COURSE");
        var query = $"/api/catalog/courses/search?categoryId={category.Id}";

        var beforeApproveResponse = await _client.GetFromJsonAsync<SearchCoursesResponse>(query, JsonOptions);
        Assert.DoesNotContain(beforeApproveResponse!.Results.Items, c => c.Id == course.Id);

        using var approveResponse = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Post, $"/api/catalog/admin/courses/{course.Id}/approve", adminToken));
        Assert.Equal(HttpStatusCode.OK, approveResponse.StatusCode);

        var afterApproveResponse = await _client.GetFromJsonAsync<SearchCoursesResponse>(query, JsonOptions);
        Assert.Contains(afterApproveResponse!.Results.Items, c => c.Id == course.Id);
    }

    /// <summary>
    /// Regression test (audit fix, 2026-09-01): UnpublishCourseHandler used to move a course out of
    /// public visibility without evicting <see cref="CourseOutputCache.Tag"/> — the same class of gap
    /// <see cref="SearchThenApprove_NewlyPublishedCourseAppearsInSearchDespiteEarlierCachedResult"/> proves
    /// is closed for the opposite (Draft→Published) transition. Same black-box technique: search while
    /// published (populating a cached "found" result), unpublish, search again with the identical query,
    /// and assert the course is now gone. If the eviction call were missing, this test would still see the
    /// stale cached result and fail.
    /// </summary>
    [Fact]
    public async Task SearchThenUnpublish_CourseDisappearsFromSearchDespiteEarlierCachedResult()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var instructor = await CreateApprovedInstructorAsync(scope.ServiceProvider, dbContext);
        var adminToken = await CreateAdminAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var course = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, instructor.Id, category.Id, "Unpublish Cache Invalidation COURSE");
        var query = $"/api/catalog/courses/search?categoryId={category.Id}";

        var beforeUnpublishResponse = await _client.GetFromJsonAsync<SearchCoursesResponse>(query, JsonOptions);
        Assert.Contains(beforeUnpublishResponse!.Results.Items, c => c.Id == course.Id);

        using var unpublishRequest = AuthenticatedRequest(HttpMethod.Post, $"/api/catalog/admin/courses/{course.Id}/unpublish", adminToken);
        unpublishRequest.Content = JsonContent.Create(new UnpublishCourseCommand("ละเมิดนโยบายเนื้อหา"));
        using var unpublishResponse = await _client.SendAsync(unpublishRequest);
        Assert.Equal(HttpStatusCode.OK, unpublishResponse.StatusCode);

        var afterUnpublishResponse = await _client.GetFromJsonAsync<SearchCoursesResponse>(query, JsonOptions);
        Assert.DoesNotContain(afterUnpublishResponse!.Results.Items, c => c.Id == course.Id);
    }
}
