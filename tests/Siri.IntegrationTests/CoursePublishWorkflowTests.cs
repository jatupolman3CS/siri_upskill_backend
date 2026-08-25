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
using Siri.Modules.Catalog.Features.ApproveCourse;
using Siri.Modules.Catalog.Features.CreateCourse;
using Siri.Modules.Catalog.Features.GetPendingCourseReviews;
using Siri.Modules.Catalog.Features.RejectCourse;
using Siri.Modules.Catalog.Features.SubmitCourseForReview;
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
/// Real HTTP-level proof of P1-05's course publish workflow (Draft→InReview→Published/Rejected) — same
/// self-contained <see cref="WebApplication"/> approach every other Catalog integration test file already
/// establishes (see <see cref="CategoryManagementTests"/>'s own doc comment for why). Requires Docker
/// locally; see <see cref="ContainersFixture"/>'s own doc comment.
/// <para>
/// Fixture setup (instructor, category, course) is built the same direct-domain way
/// <see cref="CourseManagementTests"/> already does — this file's actual subject is the four workflow
/// endpoints. Attaching media to an episode is also done directly against <see cref="AppDbContext"/>
/// (course-builder endpoints are P4's, not P1-04's) — the same technique
/// <see cref="CourseManagementTests.CreatePublishedCourseAsync"/>-equivalent setup already uses.
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class CoursePublishWorkflowTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "course-publish-workflow-tests-signing-key-0123456789";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public CoursePublishWorkflowTests(ContainersFixture containers)
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

    private static async Task<User> CreateUserAsync(IServiceProvider services, AppDbContext dbContext, string email, string password)
    {
        var passwordHasher = services.GetRequiredService<IUserPasswordHasher>();
        var clock = services.GetRequiredService<IClock>();

        var normalizedEmail = email.ToUpperInvariant();
        var throwaway = User.Register(email, normalizedEmail, "placeholder", "Test User");
        var hash = passwordHasher.HashPassword(throwaway, password);
        var user = User.Register(email, normalizedEmail, hash, "Test User");
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

    private async Task<(InstructorProfile Profile, string AccessToken)> CreateApprovedInstructorAndLoginAsync(
        IServiceProvider services, AppDbContext dbContext)
    {
        var clock = services.GetRequiredService<IClock>();
        var email = $"instructor-{Guid.NewGuid():N}@example.test";
        var user = await CreateUserAsync(services, dbContext, email, KnownPassword);

        user.AssignRole(new Role(Role.InstructorId, Role.InstructorName));
        var profile = InstructorProfile.Apply(user.Id, "Test Instructor", "Headline", "Bio");
        profile.Approve(clock);
        dbContext.InstructorProfiles().Add(profile);
        await dbContext.SaveChangesAsync();

        var token = await LoginAndGetAccessTokenAsync(services, email);
        return (profile, token);
    }

    private static async Task<string> CreateAdminAndLoginAsync(IServiceProvider services, AppDbContext dbContext)
    {
        var email = $"admin-{Guid.NewGuid():N}@example.test";
        var user = await CreateUserAsync(services, dbContext, email, KnownPassword);
        user.AssignRole(new Role(Role.AdminId, Role.AdminName));
        await dbContext.SaveChangesAsync();
        return await LoginAndGetAccessTokenAsync(services, email);
    }

    private static async Task<Category> CreateCategoryAsync(AppDbContext dbContext)
    {
        var category = Category.Create($"category-{Guid.NewGuid():N}", "หมวดหมู่ทดสอบ", "Test Category", null, null, 0);
        dbContext.Categories().Add(category);
        await dbContext.SaveChangesAsync();
        return category;
    }

    private static HttpRequestMessage AuthenticatedRequest(HttpMethod method, string path, string accessToken)
    {
        var request = new HttpRequestMessage(method, path);
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", accessToken);
        return request;
    }

    private async Task<CreateCourseResponse> CreateCourseAsync(string instructorToken, Guid categoryId)
    {
        using var request = AuthenticatedRequest(HttpMethod.Post, "/api/catalog/instructor/courses", instructorToken);
        request.Content = JsonContent.Create(new CreateCourseCommand(
            $"Course {Guid.NewGuid():N}", categoryId, CourseLevel.Beginner, CourseLanguage.Thai, 990m));

        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<CreateCourseResponse>(JsonOptions);
        Assert.NotNull(body);
        return body!;
    }

    /// <summary>Attaches a section + episode + media directly against the domain — no course-builder
    /// endpoint exists yet (P4's job), so this is the only way to make a course eligible for
    /// <c>SubmitForReview</c>/<c>Publish</c>'s "has episode with media" invariant.</summary>
    private async Task AttachMediaToCourseAsync(Guid courseId)
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = await dbContext.Courses()
            .Include(c => c.Sections).ThenInclude(s => s.Episodes)
            .SingleAsync(c => c.Id == courseId);

        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);

        await dbContext.SaveChangesAsync();
    }

    private async Task<SubmitCourseForReviewResponse> SubmitCourseForReviewAsync(string instructorToken, Guid courseId)
    {
        using var response = await _client.SendAsync(AuthenticatedRequest(
            HttpMethod.Post, $"/api/catalog/instructor/courses/{courseId}/submit-for-review", instructorToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<SubmitCourseForReviewResponse>(JsonOptions);
        Assert.NotNull(body);
        return body!;
    }

    /// <summary>Creates a course, attaches media, and submits it for review — the common starting point
    /// for every Approve/Reject test below.</summary>
    private async Task<Guid> CreateInReviewCourseAsync(string instructorToken, Guid categoryId)
    {
        var created = await CreateCourseAsync(instructorToken, categoryId);
        await AttachMediaToCourseAsync(created.Id);
        await SubmitCourseForReviewAsync(instructorToken, created.Id);
        return created.Id;
    }

    // ---- SubmitCourseForReview ------------------------------------------------------------------

    [Fact]
    public async Task SubmitCourseForReview_DraftCourseWithMedia_TransitionsToInReview()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var created = await CreateCourseAsync(instructorToken, category.Id);
        await AttachMediaToCourseAsync(created.Id);

        var response = await SubmitCourseForReviewAsync(instructorToken, created.Id);

        Assert.Equal(CourseStatus.InReview, response.Status);
    }

    [Fact]
    public async Task SubmitCourseForReview_NoEpisodeWithMedia_Returns400ValidationProblem()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var created = await CreateCourseAsync(instructorToken, category.Id); // no media attached

        using var response = await _client.SendAsync(AuthenticatedRequest(
            HttpMethod.Post, $"/api/catalog/instructor/courses/{created.Id}/submit-for-review", instructorToken));

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task SubmitCourseForReview_AnotherInstructorsCourse_Returns403Forbidden()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, ownerToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var (_, otherToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var created = await CreateCourseAsync(ownerToken, category.Id);
        await AttachMediaToCourseAsync(created.Id);

        using var response = await _client.SendAsync(AuthenticatedRequest(
            HttpMethod.Post, $"/api/catalog/instructor/courses/{created.Id}/submit-for-review", otherToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task SubmitCourseForReview_UnknownId_Returns404NotFound()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);

        using var response = await _client.SendAsync(AuthenticatedRequest(
            HttpMethod.Post, $"/api/catalog/instructor/courses/{Guid.NewGuid()}/submit-for-review", instructorToken));

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task SubmitCourseForReview_AfterRejection_ClearsRejectionReasonAndReturnsToInReview()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var adminToken = await CreateAdminAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var courseId = await CreateInReviewCourseAsync(instructorToken, category.Id);
        using var rejectRequest = AuthenticatedRequest(HttpMethod.Post, $"/api/catalog/admin/courses/{courseId}/reject", adminToken);
        rejectRequest.Content = JsonContent.Create(new RejectCourseCommand("แก้คำอธิบายก่อน"));
        await _client.SendAsync(rejectRequest);

        var resubmitted = await SubmitCourseForReviewAsync(instructorToken, courseId);

        Assert.Equal(CourseStatus.InReview, resubmitted.Status);
        var row = await dbContext.Courses().AsNoTracking().SingleAsync(c => c.Id == courseId);
        Assert.Null(row.RejectionReason);
    }

    // ---- GetPendingCourseReviews (admin) -----------------------------------------------------------

    [Fact]
    public async Task GetPendingCourseReviews_AsAdmin_ListsTheInReviewCourse()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var adminToken = await CreateAdminAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var courseId = await CreateInReviewCourseAsync(instructorToken, category.Id);

        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Get, "/api/catalog/admin/courses/pending", adminToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var page = await response.Content.ReadFromJsonAsync<PagedResult<PendingCourseReviewSummary>>(JsonOptions);
        Assert.Contains(page!.Items, c => c.Id == courseId);
    }

    [Fact]
    public async Task GetPendingCourseReviews_AsNonAdmin_Returns403Forbidden()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);

        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Get, "/api/catalog/admin/courses/pending", instructorToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- ApproveCourse (admin) ---------------------------------------------------------------------

    [Fact]
    public async Task ApproveCourse_InReviewCourse_PublishesItWithPublishedAtUtcSet()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var adminToken = await CreateAdminAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var courseId = await CreateInReviewCourseAsync(instructorToken, category.Id);

        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Post, $"/api/catalog/admin/courses/{courseId}/approve", adminToken));
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ApproveCourseResponse>(JsonOptions);
        Assert.Equal(CourseStatus.Published, body!.Status);
        Assert.NotNull(body.PublishedAtUtc);

        var row = await dbContext.Courses().AsNoTracking().SingleAsync(c => c.Id == courseId);
        Assert.Equal(CourseStatus.Published, row.Status);
    }

    [Fact]
    public async Task ApproveCourse_DraftCourse_Returns409Conflict()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var adminToken = await CreateAdminAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var created = await CreateCourseAsync(instructorToken, category.Id); // still Draft — never submitted

        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Post, $"/api/catalog/admin/courses/{created.Id}/approve", adminToken));

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task ApproveCourse_AsNonAdmin_Returns403Forbidden()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var courseId = await CreateInReviewCourseAsync(instructorToken, category.Id);

        using var response = await _client.SendAsync(
            AuthenticatedRequest(HttpMethod.Post, $"/api/catalog/admin/courses/{courseId}/approve", instructorToken));

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    // ---- RejectCourse (admin) ----------------------------------------------------------------------

    [Fact]
    public async Task RejectCourse_InReviewCourse_SetsRejectedStatusAndReason()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var adminToken = await CreateAdminAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var courseId = await CreateInReviewCourseAsync(instructorToken, category.Id);

        using var request = AuthenticatedRequest(HttpMethod.Post, $"/api/catalog/admin/courses/{courseId}/reject", adminToken);
        request.Content = JsonContent.Create(new RejectCourseCommand("คำอธิบายไม่ครบถ้วน"));
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<RejectCourseResponse>(JsonOptions);
        Assert.Equal(CourseStatus.Rejected, body!.Status);
        Assert.Equal("คำอธิบายไม่ครบถ้วน", body.RejectionReason);

        var row = await dbContext.Courses().AsNoTracking().SingleAsync(c => c.Id == courseId);
        Assert.Equal("คำอธิบายไม่ครบถ้วน", row.RejectionReason);
    }

    [Fact]
    public async Task RejectCourse_DraftCourse_Returns409Conflict()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var adminToken = await CreateAdminAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var created = await CreateCourseAsync(instructorToken, category.Id); // still Draft

        using var request = AuthenticatedRequest(HttpMethod.Post, $"/api/catalog/admin/courses/{created.Id}/reject", adminToken);
        request.Content = JsonContent.Create(new RejectCourseCommand("เหตุผล"));
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task RejectCourse_EmptyReason_Returns400ValidationProblem()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, instructorToken) = await CreateApprovedInstructorAndLoginAsync(scope.ServiceProvider, dbContext);
        var adminToken = await CreateAdminAndLoginAsync(scope.ServiceProvider, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var courseId = await CreateInReviewCourseAsync(instructorToken, category.Id);

        using var request = AuthenticatedRequest(HttpMethod.Post, $"/api/catalog/admin/courses/{courseId}/reject", adminToken);
        request.Content = JsonContent.Create(new RejectCourseCommand(""));
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    // ---- Group-level authorization ------------------------------------------------------------------

    [Theory]
    [InlineData("GET", "/api/catalog/admin/courses/pending")]
    public async Task AdminCourseEndpoint_NoAuthorizationHeader_Returns401(string method, string path)
    {
        using var request = new HttpRequestMessage(new HttpMethod(method), path);
        using var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
