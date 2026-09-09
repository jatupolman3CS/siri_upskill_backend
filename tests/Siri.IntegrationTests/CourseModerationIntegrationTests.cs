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
using Xunit;

namespace Siri.IntegrationTests;

[Collection(ContainersCollection.Name)]
public sealed class CourseModerationIntegrationTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "course-moderation-integration-tests-signing-key-0123456789";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    private Guid _adminUserId;
    private Guid _instructorUserId;
    private Guid _learnerUserId;
    private Guid _publishedCourseId;
    private Guid _draftCourseId;

    public CourseModerationIntegrationTests(ContainersFixture containers)
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
        await SeedDataAsync(scope.ServiceProvider, dbContext);
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task UnpublishCourse_AnonymousOrNonAdmin_ReturnsUnauthorizedOrForbidden()
    {
        var command = new UnpublishCourseCommand("Violates terms of service");

        // Anonymous
        var anonResponse = await _client.PostAsJsonAsync($"/api/catalog/admin/courses/{_publishedCourseId}/unpublish", command, JsonOptions);
        Assert.Equal(HttpStatusCode.Unauthorized, anonResponse.StatusCode);

        // Instructor role (cannot unpublish via admin route)
        var instToken = await LoginAndGetAccessTokenAsync(_app.Services, "instructor-mod@example.test");
        var instClient = _app.GetTestClient();
        instClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", instToken);

        var instResponse = await instClient.PostAsJsonAsync($"/api/catalog/admin/courses/{_publishedCourseId}/unpublish", command, JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, instResponse.StatusCode);

        // Learner role
        var learnerToken = await LoginAndGetAccessTokenAsync(_app.Services, "learner-mod@example.test");
        var learnerClient = _app.GetTestClient();
        learnerClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", learnerToken);

        var learnerResponse = await learnerClient.PostAsJsonAsync($"/api/catalog/admin/courses/{_publishedCourseId}/unpublish", command, JsonOptions);
        Assert.Equal(HttpStatusCode.Forbidden, learnerResponse.StatusCode);
    }

    [Fact]
    public async Task UnpublishCourse_DraftCourse_ReturnsConflict()
    {
        var adminToken = await LoginAndGetAccessTokenAsync(_app.Services, "admin-mod@example.test");
        var adminClient = _app.GetTestClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var command = new UnpublishCourseCommand("Violates terms of service");
        var response = await adminClient.PostAsJsonAsync($"/api/catalog/admin/courses/{_draftCourseId}/unpublish", command, JsonOptions);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task UnpublishCourse_PublishedCourse_AdminRole_UnpublishesWritesAuditAndNotifiesInstructor()
    {
        var adminToken = await LoginAndGetAccessTokenAsync(_app.Services, "admin-mod@example.test");
        var adminClient = _app.GetTestClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", adminToken);

        var reason = "Copyright infringement report verified";
        var command = new UnpublishCourseCommand(reason);

        var response = await adminClient.PostAsJsonAsync($"/api/catalog/admin/courses/{_publishedCourseId}/unpublish", command, JsonOptions);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<UnpublishCourseResponse>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal(_publishedCourseId, result.Id);
        Assert.Equal(CourseStatus.Draft, result.Status);
        Assert.Equal(reason, result.Reason);

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // Verify COURSE state
        var course = await db.Courses().FindAsync(_publishedCourseId);
        Assert.NotNull(course);
        Assert.Equal(CourseStatus.Draft, course.Status);
        Assert.Equal(reason, course.RejectionReason);

        // Verify Security Audit
        var audit = await db.SecurityAudits()
            .FirstOrDefaultAsync(a => a.EventType == "course.unpublished" && a.UserId == _adminUserId);
        Assert.NotNull(audit);
        Assert.Contains(reason, audit.Detail);

        // Verify Notification Email Outbox
        var outbox = await db.Set<Siri.Modules.Notification.Domain.EMAIL_OUTBOX_MESSAGE>()
            .FirstOrDefaultAsync(e => e.TemplateKey == "course-unpublished-notification");
        Assert.NotNull(outbox);
        Assert.Equal("instructor-mod@example.test", outbox.ToEmail);
        Assert.Contains(reason, outbox.BodyHtml);
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

    private async Task SeedDataAsync(IServiceProvider services, AppDbContext db)
    {
        var clock = services.GetRequiredService<IClock>();

        var adminUser = await CreateUserAsync(services, db, "admin-mod@example.test", KnownPassword);
        adminUser.AssignRole(new ROLE(ROLE.AdminId, ROLE.AdminName));

        var instructorUser = await CreateUserAsync(services, db, "instructor-mod@example.test", KnownPassword);
        instructorUser.AssignRole(new ROLE(ROLE.InstructorId, ROLE.InstructorName));

        var learnerUser = await CreateUserAsync(services, db, "learner-mod@example.test", KnownPassword);
        learnerUser.AssignRole(new ROLE(ROLE.LearnerId, ROLE.LearnerName));

        await db.SaveChangesAsync();

        _adminUserId = adminUser.Id;
        _instructorUserId = instructorUser.Id;
        _learnerUserId = learnerUser.Id;

        var profile = INSTRUCTOR_PROFILE.Apply(instructorUser.Id, "Instructor Mod Display", "Headline", "Bio");
        profile.Approve(clock);
        db.InstructorProfiles().Add(profile);

        var category = CATEGORY.Create("moderation-cat", "Moderation Cat", "Moderation Cat En", null, null, 0);
        db.Categories().Add(category);
        await db.SaveChangesAsync();

        var publishedCourse = COURSE.Create("course-to-unpublish", "COURSE To Unpublish", profile.Id, category.Id, CourseLevel.Beginner, CourseLanguage.Thai, 990m);
        var s1 = publishedCourse.AddSection("Sec 1");
        s1.AddEpisode("Ep 1", null, false).AttachMedia(Guid.NewGuid(), 600);
        publishedCourse.SubmitForReview();
        publishedCourse.Publish(clock);

        var draftCourse = COURSE.Create("draft-course", "Draft COURSE", profile.Id, category.Id, CourseLevel.Beginner, CourseLanguage.Thai, 990m);

        db.Courses().AddRange(publishedCourse, draftCourse);
        await db.SaveChangesAsync();

        _publishedCourseId = publishedCourse.Id;
        _draftCourseId = draftCourse.Id;
    }
}
