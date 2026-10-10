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
using Siri.Modules.Catalog.Features.AddEpisodeAttachment;
using Siri.Modules.Catalog.Features.DownloadEpisodeAttachment;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Learning;
using Siri.Modules.Learning.Domain;
using Siri.Modules.Learning.Infrastructure;
using Siri.Modules.Notification;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// Integration tests verifying access control for episode attachments (P4-03a).
/// Ensures IDOR prevention, 404 responses for unauthorized users, and removal of StorageKey exposure.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class EpisodeAttachmentAccessTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "episode-attachment-access-tests-signing-key-0123456789";

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    private Guid _instructorUserId;
    private Guid _enrolledUserId;
    private Guid _unenrolledUserId;

    private Guid _courseId;
    private Guid _paidEpisodeId;
    private Guid _previewEpisodeId;
    private Guid _paidAttachmentId;
    private Guid _previewAttachmentId;

    public EpisodeAttachmentAccessTests(ContainersFixture containers)
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
        // The download endpoint mints a signed R2 link: swap the real adapter (needs a bucket) for an in-memory double
        // BEFORE AddCatalogModule, whose AddFileStorage only registers the real one when none is present.
        builder.Services.AddSingleton<Siri.Integrations.Storage.IFileStorage>(new Siri.IntegrationTests.TestData.InMemoryFileStorage());
        builder.Services.AddCatalogModule(builder.Configuration);
        builder.Services.AddLearningModule();

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

    private async Task SeedDataAsync(IServiceProvider services, AppDbContext dbContext)
    {
        var clock = services.GetRequiredService<IClock>();

        // 1. Users
        var instructor = await CreateUserAsync(services, dbContext, $"instructor_{Guid.NewGuid():N}@test.com");
        _instructorUserId = instructor.Id;

        var enrolledStudent = await CreateUserAsync(services, dbContext, $"enrolled_{Guid.NewGuid():N}@test.com");
        _enrolledUserId = enrolledStudent.Id;

        var unenrolledStudent = await CreateUserAsync(services, dbContext, $"unenrolled_{Guid.NewGuid():N}@test.com");
        _unenrolledUserId = unenrolledStudent.Id;

        // 2. Instructor Profile & CATEGORY & COURSE
        var profile = INSTRUCTOR_PROFILE.Apply(instructor.Id, "Instructor attachment test", "Headline", "Bio");
        profile.Approve(clock);
        dbContext.InstructorProfiles().Add(profile);

        var category = CATEGORY.Create($"category-{Guid.NewGuid():N}", "หมวดหมู่ทดสอบ", "Test CATEGORY", null, null, 0);
        dbContext.Categories().Add(category);

        var course = COURSE.Create($"attachment-security-{Guid.NewGuid():N}", "Attachment Security COURSE", profile.Id, category.Id, CourseLevel.Beginner, CourseLanguage.Thai, 990m);
        _courseId = course.Id;
        dbContext.Courses().Add(course);

        var section = course.AddSection("Section 1");
        var paidEpisode = section.AddEpisode("Paid Episode", "Desc", isFreePreview: false);
        _paidEpisodeId = paidEpisode.Id;

        var previewEpisode = section.AddEpisode("Preview Episode", "Desc", isFreePreview: true);
        _previewEpisodeId = previewEpisode.Id;

        // 3. Attachments
        var paidAttachment = EPISODE_ATTACHMENT.Create(
            paidEpisode.Id, "secret-handout.pdf", "attachments/secrets/secret-handout.pdf", "application/pdf", 51200);
        _paidAttachmentId = paidAttachment.Id;
        dbContext.EpisodeAttachments().Add(paidAttachment);

        var previewAttachment = EPISODE_ATTACHMENT.Create(
            previewEpisode.Id, "preview-handout.pdf", "attachments/previews/preview-handout.pdf", "application/pdf", 25600);
        _previewAttachmentId = previewAttachment.Id;
        dbContext.EpisodeAttachments().Add(previewAttachment);

        // 4. Enrollment for enrolled student
        var enrollment = ENROLLMENT.Create(
            _enrolledUserId, course.Id, null, EnrollmentSource.Purchase, null, clock);
        dbContext.Enrollments().Add(enrollment);

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
    public async Task GetEpisodeAttachments_UnauthenticatedOnPaidEpisode_ReturnsNotFound()
    {
        // Act
        var response = await _client.GetAsync($"/api/catalog/episodes/{_paidEpisodeId}/attachments");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetEpisodeAttachments_AuthenticatedUnenrolledUserOnPaidEpisode_ReturnsNotFound()
    {
        // Arrange
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users().FirstAsync(u => u.Id == _unenrolledUserId);
        var token = await LoginAndGetAccessTokenAsync(user.Email);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/catalog/episodes/{_paidEpisodeId}/attachments");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetEpisodeAttachments_EnrolledUserOnPaidEpisode_Returns200WithAttachments()
    {
        // Arrange
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users().FirstAsync(u => u.Id == _enrolledUserId);
        var token = await LoginAndGetAccessTokenAsync(user.Email);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/catalog/episodes/{_paidEpisodeId}/attachments");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var attachments = await response.Content.ReadFromJsonAsync<IReadOnlyList<EpisodeAttachmentResponse>>();
        Assert.NotNull(attachments);
        Assert.Single(attachments);
        Assert.Equal("secret-handout.pdf", attachments[0].FileName);
    }

    [Fact]
    public async Task GetEpisodeAttachments_UnauthenticatedOnFreePreviewEpisode_Returns200WithAttachments()
    {
        // Act
        var response = await _client.GetAsync($"/api/catalog/episodes/{_previewEpisodeId}/attachments");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var attachments = await response.Content.ReadFromJsonAsync<IReadOnlyList<EpisodeAttachmentResponse>>();
        Assert.NotNull(attachments);
        Assert.Single(attachments);
        Assert.Equal("preview-handout.pdf", attachments[0].FileName);
    }

    [Fact]
    public async Task GetEpisodeAttachments_Response_DoesNotContainStorageKeyInJson()
    {
        // Arrange
        var response = await _client.GetAsync($"/api/catalog/episodes/{_previewEpisodeId}/attachments");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        // Act
        var rawJson = await response.Content.ReadAsStringAsync();

        // Assert
        Assert.Contains("preview-handout.pdf", rawJson);
        Assert.DoesNotContain("storageKey", rawJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("attachments/previews", rawJson, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task DownloadEpisodeAttachment_UnenrolledUser_ReturnsNotFound()
    {
        // Arrange
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users().FirstAsync(u => u.Id == _unenrolledUserId);
        var token = await LoginAndGetAccessTokenAsync(user.Email);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/catalog/episodes/{_paidEpisodeId}/attachments/{_paidAttachmentId}/download");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task DownloadEpisodeAttachment_EnrolledUser_Returns200WithoutStorageKey()
    {
        // Arrange
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users().FirstAsync(u => u.Id == _enrolledUserId);
        var token = await LoginAndGetAccessTokenAsync(user.Email);

        var request = new HttpRequestMessage(HttpMethod.Get, $"/api/catalog/episodes/{_paidEpisodeId}/attachments/{_paidAttachmentId}/download");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        // Act
        var response = await _client.SendAsync(request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var rawJson = await response.Content.ReadAsStringAsync();
        Assert.Contains("secret-handout.pdf", rawJson);
        Assert.DoesNotContain("storageKey", rawJson, StringComparison.OrdinalIgnoreCase);
        // The signed link necessarily contains the (server-generated) object key; what must never appear is the key as a field of its own.
        Assert.StartsWith("https://r2.example.test/", System.Text.Json.JsonDocument.Parse(rawJson).RootElement.GetProperty("downloadUrl").GetString());
    }
}
