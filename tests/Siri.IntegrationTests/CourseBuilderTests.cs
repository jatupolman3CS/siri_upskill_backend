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
using Siri.Modules.Catalog.Features.AutosaveCourse;
using Siri.Modules.Catalog.Features.CreateCourse;
using Siri.Modules.Catalog.Features.CreateCourseEpisode;
using Siri.Modules.Catalog.Features.CreateCourseSection;
using Siri.Modules.Catalog.Features.GetCourseBuilder;
using Siri.Modules.Catalog.Features.ReorderCourseEpisodes;
using Siri.Modules.Catalog.Features.ReorderCourseSections;
using Siri.Modules.Catalog.Features.UpdateCourseEpisode;
using Siri.Modules.Catalog.Features.UpdateCourseSection;
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
/// Integration tests for P4-01 COURSE Builder API:
/// Section/Episode CRUD & reorder, optimistic concurrency autosave, ownership checks, and status guards.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class CourseBuilderTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "course-builder-tests-signing-key-0123456789012";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public CourseBuilderTests(ContainersFixture containers)
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

    private static async Task<CATEGORY> CreateCategoryAsync(AppDbContext dbContext)
    {
        var category = CATEGORY.Create($"category-{Guid.NewGuid():N}", "หมวดหมู่ทดสอบ", "Test CATEGORY", null, null, 0);
        dbContext.Categories().Add(category);
        await dbContext.SaveChangesAsync();
        return category;
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

    // ---- GetCourseBuilder -----------------------------------------------------------------------

    [Fact]
    public async Task GetCourseBuilder_OwnDraftCourse_ReturnsFullStructureAndRowVersion()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var created = await CreateCourseAsync(instructorToken, category.Id, "Builder COURSE");

        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Get, $"/api/catalog/instructor/courses/{created.Id}/builder", instructorToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CourseBuilderResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal("Builder COURSE", body!.Title);
        Assert.NotNull(body.RowVersion);
        Assert.NotEmpty(body.RowVersion);
    }

    [Fact]
    public async Task GetCourseBuilder_AnotherInstructorsCourse_Returns403Forbidden()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, ownerToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var (_, otherToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var created = await CreateCourseAsync(ownerToken, category.Id);

        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Get, $"/api/catalog/instructor/courses/{created.Id}/builder", otherToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- Section CRUD & Reorder -----------------------------------------------------------------

    [Fact]
    public async Task CreateCourseSection_OwnDraftCourse_CreatesAndAppendsSection()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var created = await CreateCourseAsync(instructorToken, category.Id);

        using var request = AuthenticatedRequest(HttpMethod.Post, $"/api/catalog/instructor/courses/{created.Id}/sections", instructorToken);
        request.Content = JsonContent.Create(new CreateCourseSectionCommand("New Section 1"));
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var section = await response.Content.ReadFromJsonAsync<CourseSectionResponse>(JsonOptions);
        Assert.NotNull(section);
        Assert.Equal("New Section 1", section!.Title);
        Assert.Equal(0, section.SortOrder);
    }

    [Fact]
    public async Task UpdateCourseSection_OwnDraftCourse_RenamesSection()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var created = await CreateCourseAsync(instructorToken, category.Id);

        // Add section
        using var addReq = AuthenticatedRequest(HttpMethod.Post, $"/api/catalog/instructor/courses/{created.Id}/sections", instructorToken);
        addReq.Content = JsonContent.Create(new CreateCourseSectionCommand("Initial Title"));
        using var addRes = await _client.SendAsync(addReq);
        var section = await addRes.Content.ReadFromJsonAsync<CourseSectionResponse>(JsonOptions);

        // Update section
        using var updateReq = AuthenticatedRequest(HttpMethod.Put, $"/api/catalog/instructor/courses/{created.Id}/sections/{section!.Id}", instructorToken);
        updateReq.Content = JsonContent.Create(new UpdateCourseSectionCommand("Updated Section Title"));
        using var updateRes = await _client.SendAsync(updateReq);

        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        var updated = await updateRes.Content.ReadFromJsonAsync<CourseSectionResponse>(JsonOptions);
        Assert.Equal("Updated Section Title", updated!.Title);
    }

    [Fact]
    public async Task DeleteCourseSection_OwnDraftCourse_RemovesSection()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var created = await CreateCourseAsync(instructorToken, category.Id);

        // Add section
        using var addReq = AuthenticatedRequest(HttpMethod.Post, $"/api/catalog/instructor/courses/{created.Id}/sections", instructorToken);
        addReq.Content = JsonContent.Create(new CreateCourseSectionCommand("To Delete"));
        using var addRes = await _client.SendAsync(addReq);
        var section = await addRes.Content.ReadFromJsonAsync<CourseSectionResponse>(JsonOptions);

        // Delete section
        using var delReq = AuthenticatedRequest(HttpMethod.Delete, $"/api/catalog/instructor/courses/{created.Id}/sections/{section!.Id}", instructorToken);
        using var delRes = await _client.SendAsync(delReq);

        Assert.Equal(HttpStatusCode.NoContent, delRes.StatusCode);

        // Verify gone
        var exists = await dbContext.CourseSections().AsNoTracking().AnyAsync(s => s.Id == section.Id);
        Assert.False(exists);
    }

    [Fact]
    public async Task ReorderCourseSections_FullSiblingSet_ReordersSuccessfully()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var created = await CreateCourseAsync(instructorToken, category.Id);

        // Add 2 sections
        using var addReq1 = AuthenticatedRequest(HttpMethod.Post, $"/api/catalog/instructor/courses/{created.Id}/sections", instructorToken);
        addReq1.Content = JsonContent.Create(new CreateCourseSectionCommand("Section 1"));
        using var addRes1 = await _client.SendAsync(addReq1);
        var sec1 = await addRes1.Content.ReadFromJsonAsync<CourseSectionResponse>(JsonOptions);

        using var addReq2 = AuthenticatedRequest(HttpMethod.Post, $"/api/catalog/instructor/courses/{created.Id}/sections", instructorToken);
        addReq2.Content = JsonContent.Create(new CreateCourseSectionCommand("Section 2"));
        using var addRes2 = await _client.SendAsync(addReq2);
        var sec2 = await addRes2.Content.ReadFromJsonAsync<CourseSectionResponse>(JsonOptions);

        // Reorder (swap)
        using var reorderReq = AuthenticatedRequest(HttpMethod.Put, $"/api/catalog/instructor/courses/{created.Id}/sections/reorder", instructorToken);
        reorderReq.Content = JsonContent.Create(new ReorderCourseSectionsCommand([
            new ReorderCourseSectionItem(sec2!.Id, 0),
            new ReorderCourseSectionItem(sec1!.Id, 1),
        ]));
        using var reorderRes = await _client.SendAsync(reorderReq);

        Assert.Equal(HttpStatusCode.OK, reorderRes.StatusCode);

        var sec1Db = await dbContext.CourseSections().AsNoTracking().SingleAsync(s => s.Id == sec1.Id);
        var sec2Db = await dbContext.CourseSections().AsNoTracking().SingleAsync(s => s.Id == sec2.Id);

        Assert.Equal(1, sec1Db.SortOrder);
        Assert.Equal(0, sec2Db.SortOrder);
    }

    [Fact]
    public async Task ReorderCourseSections_PartialSet_Returns400BadRequest()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var created = await CreateCourseAsync(instructorToken, category.Id);

        // Add 2 sections
        using var addReq1 = AuthenticatedRequest(HttpMethod.Post, $"/api/catalog/instructor/courses/{created.Id}/sections", instructorToken);
        addReq1.Content = JsonContent.Create(new CreateCourseSectionCommand("Section 1"));
        using var addRes1 = await _client.SendAsync(addReq1);
        var sec1 = await addRes1.Content.ReadFromJsonAsync<CourseSectionResponse>(JsonOptions);

        using var addReq2 = AuthenticatedRequest(HttpMethod.Post, $"/api/catalog/instructor/courses/{created.Id}/sections", instructorToken);
        addReq2.Content = JsonContent.Create(new CreateCourseSectionCommand("Section 2"));
        await _client.SendAsync(addReq2);

        // Submit only 1 section in reorder
        using var reorderReq = AuthenticatedRequest(HttpMethod.Put, $"/api/catalog/instructor/courses/{created.Id}/sections/reorder", instructorToken);
        reorderReq.Content = JsonContent.Create(new ReorderCourseSectionsCommand([
            new ReorderCourseSectionItem(sec1!.Id, 0),
        ]));
        using var reorderRes = await _client.SendAsync(reorderReq);

        Assert.Equal(HttpStatusCode.BadRequest, reorderRes.StatusCode);
    }

    // ---- Episode CRUD & Reorder -----------------------------------------------------------------

    [Fact]
    public async Task CreateCourseEpisode_OwnDraftCourse_CreatesEpisode()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var created = await CreateCourseAsync(instructorToken, category.Id);

        // Add section
        using var addSecReq = AuthenticatedRequest(HttpMethod.Post, $"/api/catalog/instructor/courses/{created.Id}/sections", instructorToken);
        addSecReq.Content = JsonContent.Create(new CreateCourseSectionCommand("Section 1"));
        using var addSecRes = await _client.SendAsync(addSecReq);
        var sec = await addSecRes.Content.ReadFromJsonAsync<CourseSectionResponse>(JsonOptions);

        // Add episode
        using var addEpReq = AuthenticatedRequest(HttpMethod.Post, $"/api/catalog/instructor/courses/{created.Id}/sections/{sec!.Id}/episodes", instructorToken);
        addEpReq.Content = JsonContent.Create(new CreateCourseEpisodeCommand("Lesson 1", "Description", true));
        using var addEpRes = await _client.SendAsync(addEpReq);

        Assert.Equal(HttpStatusCode.Created, addEpRes.StatusCode);
        var ep = await addEpRes.Content.ReadFromJsonAsync<CourseEpisodeResponse>(JsonOptions);
        Assert.NotNull(ep);
        Assert.Equal("Lesson 1", ep!.Title);
        Assert.True(ep.IsFreePreview);
        Assert.Equal(0, ep.SortOrder);
    }

    [Fact]
    public async Task ReorderCourseEpisodes_FullSiblingSet_ReordersSuccessfully()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var created = await CreateCourseAsync(instructorToken, category.Id);

        // Add section
        using var addSecReq = AuthenticatedRequest(HttpMethod.Post, $"/api/catalog/instructor/courses/{created.Id}/sections", instructorToken);
        addSecReq.Content = JsonContent.Create(new CreateCourseSectionCommand("Section 1"));
        using var addSecRes = await _client.SendAsync(addSecReq);
        var sec = await addSecRes.Content.ReadFromJsonAsync<CourseSectionResponse>(JsonOptions);

        // Add 2 episodes
        using var addEpReq1 = AuthenticatedRequest(HttpMethod.Post, $"/api/catalog/instructor/courses/{created.Id}/sections/{sec!.Id}/episodes", instructorToken);
        addEpReq1.Content = JsonContent.Create(new CreateCourseEpisodeCommand("Lesson 1", null, false));
        using var addEpRes1 = await _client.SendAsync(addEpReq1);
        var ep1 = await addEpRes1.Content.ReadFromJsonAsync<CourseEpisodeResponse>(JsonOptions);

        using var addEpReq2 = AuthenticatedRequest(HttpMethod.Post, $"/api/catalog/instructor/courses/{created.Id}/sections/{sec.Id}/episodes", instructorToken);
        addEpReq2.Content = JsonContent.Create(new CreateCourseEpisodeCommand("Lesson 2", null, false));
        using var addEpRes2 = await _client.SendAsync(addEpReq2);
        var ep2 = await addEpRes2.Content.ReadFromJsonAsync<CourseEpisodeResponse>(JsonOptions);

        // Reorder (swap)
        using var reorderReq = AuthenticatedRequest(HttpMethod.Put, $"/api/catalog/instructor/courses/{created.Id}/sections/{sec.Id}/episodes/reorder", instructorToken);
        reorderReq.Content = JsonContent.Create(new ReorderCourseEpisodesCommand([
            new ReorderCourseEpisodeItem(ep2!.Id, 0),
            new ReorderCourseEpisodeItem(ep1!.Id, 1),
        ]));
        using var reorderRes = await _client.SendAsync(reorderReq);

        Assert.Equal(HttpStatusCode.OK, reorderRes.StatusCode);

        var ep1Db = await dbContext.CourseEpisodes().AsNoTracking().SingleAsync(e => e.Id == ep1.Id);
        var ep2Db = await dbContext.CourseEpisodes().AsNoTracking().SingleAsync(e => e.Id == ep2.Id);

        Assert.Equal(1, ep1Db.SortOrder);
        Assert.Equal(0, ep2Db.SortOrder);
    }

    // ---- Autosave & Optimistic Concurrency -------------------------------------------------------

    [Fact]
    public async Task AutosaveCourse_ValidDraftCourse_SavesFullGraphAndReturnsNewRowVersion()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var created = await CreateCourseAsync(instructorToken, category.Id);

        // Get builder to read current RowVersion
        using var getReq = AuthenticatedRequest(HttpMethod.Get, $"/api/catalog/instructor/courses/{created.Id}/builder", instructorToken);
        using var getRes = await _client.SendAsync(getReq);
        var builder = await getRes.Content.ReadFromJsonAsync<CourseBuilderResponse>(JsonOptions);

        // Autosave with full graph
        using var autoReq = AuthenticatedRequest(HttpMethod.Put, $"/api/catalog/instructor/courses/{created.Id}/autosave", instructorToken);
        autoReq.Content = JsonContent.Create(new AutosaveCourseCommand(
            "Autosaved Title",
            "Autosaved Subtitle",
            "Autosaved Description",
            category.Id,
            CourseLevel.Intermediate,
            CourseLanguage.English,
            "https://example.com/cover.jpg",
            1290m,
            2490m,
            180,
            "SEO Title",
            "SEO Description",
            ["Outcome 1", "Outcome 2"],
            ["Requirement 1"],
            [
                new AutosaveSectionItem(
                    null,
                    "Autosaved Section 1",
                    0,
                    [
                        new AutosaveEpisodeItem(null, "Autosaved Ep 1", "Ep Desc", 0, true),
                    ]),
            ],
            builder!.RowVersion));

        using var autoRes = await _client.SendAsync(autoReq);
        Assert.Equal(HttpStatusCode.OK, autoRes.StatusCode);

        var autoResult = await autoRes.Content.ReadFromJsonAsync<AutosaveCourseResponse>(JsonOptions);
        Assert.NotNull(autoResult);
        Assert.NotEmpty(autoResult!.RowVersion);
    }

    [Fact]
    public async Task AutosaveCourse_StaleRowVersion_Returns409Conflict()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var created = await CreateCourseAsync(instructorToken, category.Id);

        // Intentionally provide a stale/bogus rowversion
        var staleRowVersion = new byte[] { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01 };

        using var autoReq = AuthenticatedRequest(HttpMethod.Put, $"/api/catalog/instructor/courses/{created.Id}/autosave", instructorToken);
        autoReq.Content = JsonContent.Create(new AutosaveCourseCommand(
            "Autosaved Title",
            null,
            null,
            category.Id,
            CourseLevel.Beginner,
            CourseLanguage.Thai,
            null,
            990m,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            staleRowVersion));

        using var autoRes = await _client.SendAsync(autoReq);
        Assert.Equal(HttpStatusCode.Conflict, autoRes.StatusCode);
    }

    [Fact]
    public async Task AutosaveCourse_NonDraftCourse_Returns409Conflict()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (profile, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var published = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, profile.Id, category.Id);

        using var autoReq = AuthenticatedRequest(HttpMethod.Put, $"/api/catalog/instructor/courses/{published.Id}/autosave", instructorToken);
        autoReq.Content = JsonContent.Create(new AutosaveCourseCommand(
            "New Title",
            null,
            null,
            category.Id,
            CourseLevel.Beginner,
            CourseLanguage.Thai,
            null,
            990m,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            published.RowVersion));

        using var autoRes = await _client.SendAsync(autoReq);
        Assert.Equal(HttpStatusCode.Conflict, autoRes.StatusCode);
    }
}
