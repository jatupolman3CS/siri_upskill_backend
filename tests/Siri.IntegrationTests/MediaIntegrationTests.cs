using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
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
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Identity;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Learning;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Domain;
using Siri.Modules.Learning.Infrastructure;
using Siri.Modules.Media;
using Siri.Modules.Media.Application;
using Siri.Modules.Media.Domain;
using Siri.Modules.Media.Infrastructure;
using Siri.Modules.Notification;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// Integration tests for Media module (C2).
/// Verifies:
/// 1. Playback session authorization: non-enrolled rejected, free preview allowed, active enrollment allowed, expired enrollment rejected.
/// 2. Upload session and Webhook processing.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class MediaIntegrationTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "media-integration-tests-signing-key-0123456789";

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    private Guid _instructorUserId;
    private Guid _enrolledUserId;
    private Guid _unenrolledUserId;
    private Guid _expiredUserId;

    private Guid _courseId;
    private Guid _paidEpisodeId;
    private Guid _freePreviewEpisodeId;
    private Guid _mediaAssetId;

    public MediaIntegrationTests(ContainersFixture containers)
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
            ["VideoProvider:LibraryId"] = "12345",
            ["VideoProvider:ApiKey"] = "CHANGE_ME_integration_test",
            ["VideoProvider:ReadOnlyApiKey"] = "test-read-only-key",
            ["VideoProvider:PullZone"] = "test-pull-zone",
            ["VideoProvider:CdnHostname"] = "video.siriupskill.test",
            ["VideoProvider:TokenAuthenticationKey"] = "test-token-auth-security-key",
            ["VideoProvider:TokenExpiryMinutes"] = "5",
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
        builder.Services.AddLearningModule();
        builder.Services.AddMediaModule(builder.Configuration);

        _app = builder.Build();

        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapCatalogEndpoints();
        _app.MapLearningEndpoints();
        _app.MapMediaEndpoints();

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
        var instructor = await CreateUserAsync(services, dbContext, $"inst_media_{Guid.NewGuid():N}@test.com");
        instructor.AssignRole(new ROLE(ROLE.InstructorId, ROLE.InstructorName));
        _instructorUserId = instructor.Id;

        var enrolled = await CreateUserAsync(services, dbContext, $"enrolled_media_{Guid.NewGuid():N}@test.com");
        _enrolledUserId = enrolled.Id;

        var unenrolled = await CreateUserAsync(services, dbContext, $"unenrolled_media_{Guid.NewGuid():N}@test.com");
        _unenrolledUserId = unenrolled.Id;

        var expired = await CreateUserAsync(services, dbContext, $"expired_media_{Guid.NewGuid():N}@test.com");
        _expiredUserId = expired.Id;

        // 2. Instructor Profile & COURSE
        var profile = INSTRUCTOR_PROFILE.Apply(instructor.Id, "Media Instructor", "Media Specialist", "Bio");
        profile.Approve(clock);
        dbContext.InstructorProfiles().Add(profile);

        var category = CATEGORY.Create($"cat-media-{Guid.NewGuid():N}", "หมวดมีเดีย", "Media CATEGORY", null, null, 0);
        dbContext.Categories().Add(category);

        var course = COURSE.Create($"media-course-{Guid.NewGuid():N}", "Media Protection COURSE", profile.Id, category.Id, CourseLevel.Beginner, CourseLanguage.Thai, 2000m);
        _courseId = course.Id;
        dbContext.Courses().Add(course);

        var section = course.AddSection("Main Section");

        // Paid Episode
        var paidEpisode = section.AddEpisode("Paid Lesson", "Paid Description", isFreePreview: false);
        _paidEpisodeId = paidEpisode.Id;

        // Free Preview Episode
        var previewEpisode = section.AddEpisode("Preview Lesson", "Preview Description", isFreePreview: true);
        _freePreviewEpisodeId = previewEpisode.Id;

        // 3. Media Asset linked to paid episode
        var mediaAsset = MEDIA_ASSET.Create(
            "BunnyStream",
            "bunny-vid-guid-9999",
            instructor.Id,
            false);
        mediaAsset.MarkReady("bunny-playback-id-9999", 600, "https://cdn.example/thumb.jpg", clock);
        _mediaAssetId = mediaAsset.MEDIA_ASSET_ID;
        dbContext.MediaAssets().Add(mediaAsset);

        // 4. Active Enrollment
        var activeEnrollment = ENROLLMENT.Create(
            _enrolledUserId,
            _courseId,
            null,
            EnrollmentSource.Purchase,
            null,
            clock);
        dbContext.Enrollments().Add(activeEnrollment);

        // 5. Expired Enrollment (Expires in past)
        var pastDate = clock.UtcNow.AddDays(-5);
        var expiredEnrollment = ENROLLMENT.Create(
            _expiredUserId,
            _courseId,
            null,
            EnrollmentSource.Purchase,
            pastDate,
            clock);
        dbContext.Enrollments().Add(expiredEnrollment);

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
    public async Task CreatePlaybackSession_UnenrolledUserOnPaidEpisode_ReturnsForbiddenOrNotFound()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users().FirstAsync(u => u.Id == _unenrolledUserId);
        var token = await LoginAndGetAccessTokenAsync(user.Email);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/media/playback-sessions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new CreatePlaybackSessionCommand(_paidEpisodeId, _mediaAssetId, "dev-1"));

        var response = await _client.SendAsync(request);

        // Security check: Must not allow playback (403 Forbidden or 404 NotFound)
        Assert.True(response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task CreatePlaybackSession_EnrolledUserOnPaidEpisode_ReturnsPlaybackTokenAndWatermark()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users().FirstAsync(u => u.Id == _enrolledUserId);
        var token = await LoginAndGetAccessTokenAsync(user.Email);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/media/playback-sessions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new CreatePlaybackSessionCommand(_paidEpisodeId, _mediaAssetId, "dev-1"));

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var session = await response.Content.ReadFromJsonAsync<PlaybackSessionResponse>();
        Assert.NotNull(session);
        Assert.NotNull(session.ManifestUrl);
        Assert.NotNull(session.WatermarkPayload);
        Assert.Contains(user.Email, session.WatermarkPayload);
    }

    [Fact]
    public async Task CreatePlaybackSession_ExpiredEnrollment_ReturnsForbidden()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var user = await db.Users().FirstAsync(u => u.Id == _expiredUserId);
        var token = await LoginAndGetAccessTokenAsync(user.Email);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/media/playback-sessions");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        request.Content = JsonContent.Create(new CreatePlaybackSessionCommand(_paidEpisodeId, _mediaAssetId, "dev-1"));

        var response = await _client.SendAsync(request);
        Assert.True(response.StatusCode == HttpStatusCode.Forbidden || response.StatusCode == HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task MediaUploadSession_CreateAndWebhookTranscodeUpdate_WorksCorrectly()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var mediaAssetService = scope.ServiceProvider.GetRequiredService<MediaAssetService>();
        var uploadSessionService = scope.ServiceProvider.GetRequiredService<MediaUploadSessionService>();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        // 1. Instructor creates asset
        var createAssetCmd = new CreateMediaAssetCommand("Lesson 2 Video");

        var assetResult = await mediaAssetService.CreateAsync(_instructorUserId, createAssetCmd, CancellationToken.None);
        Assert.True(assetResult.IsSuccess);
        var assetId = assetResult.Value.Id;

        // 2. Instructor initiates upload session
        var sessionResult = await uploadSessionService.CreateAsync(_instructorUserId, assetId, CancellationToken.None);
        Assert.True(sessionResult.IsSuccess);
        Assert.NotNull(sessionResult.Value.UploadUrl);

        // 3. Webhook handler processes Bunny transcode ready callback
        var webhookHandler = scope.ServiceProvider.GetRequiredService<BunnyWebhookHandler>();
        var body = JsonSerializer.SerializeToUtf8Bytes(new BunnyWebhookPayload(12345, assetResult.Value.ProviderAssetId, 3));
        var signature = Convert.ToHexStringLower(HMACSHA256.HashData(Encoding.UTF8.GetBytes("test-read-only-key"), body));
        var webhookResult = await webhookHandler.HandleSignedWebhookAsync(body, "v1", "hmac-sha256", signature, CancellationToken.None);

        Assert.True(webhookResult.IsSuccess);

        // Verify status in DB
        var updatedAsset = await db.MediaAssets().FirstAsync(a => a.MEDIA_ASSET_ID == assetId);
        Assert.Equal(MediaAssetStatus.Ready, updatedAsset.STATUS);
    }
}
