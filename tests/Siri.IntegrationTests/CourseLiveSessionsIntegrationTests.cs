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
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.CancelLiveSession;
using Siri.Modules.Catalog.Features.CreateLiveSession;
using Siri.Modules.Catalog.Features.GetCourseDetail;
using Siri.Modules.Catalog.Features.SearchCourses;
using Siri.Modules.Catalog.Features.SetCourseDeliveryFormat;
using Siri.Modules.Catalog.Features.UpdateLiveSession;
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
/// Integration tests for P11-02 (Instructor live-session API) and P11-07 (Live schedule projection & search):
/// Verifies HTTP endpoints, authorization & IDOR, duration/overlap guards, soft-cancel semantics,
/// delivery format transitions, and the security-critical zero meetUrl leakage requirement.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class CourseLiveSessionsIntegrationTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "course-live-sessions-tests-signing-key-0123456789";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public CourseLiveSessionsIntegrationTests(ContainersFixture containers)
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

        // Program.cs registers the same converter globally; without it the minimal-API enum bodies below (PUT delivery-format) cannot bind.
        builder.Services.ConfigureHttpJsonOptions(options =>
            options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter()));
        builder.Services.AddScoped<Siri.SharedKernel.Contracts.IMediaAssetContract, Siri.Modules.Media.Application.MediaAssetContractService>();

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

    private static async Task<COURSE> CreateCourseAsync(AppDbContext dbContext, Guid instructorId, Guid categoryId, DeliveryFormat format = DeliveryFormat.Live)
    {
        var course = COURSE.Create(
            $"course-{Guid.NewGuid():N}",
            "คอร์สสดสำหรับทดสอบ",
            instructorId,
            categoryId,
            CourseLevel.Beginner,
            CourseLanguage.Thai,
            1200m);

        if (format != DeliveryFormat.OnDemand)
        {
            course.SetDeliveryFormat(format);
        }

        dbContext.Courses().Add(course);
        await dbContext.SaveChangesAsync();
        return course;
    }

    // ---- P11-02: Create Live Session Tests ----------------------------------------------------

    [Fact]
    public async Task CreateLiveSession_ValidRequest_ReturnsCreated()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (profile, token) = await CreateApprovedInstructorAndLoginAsync(_app.Services, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var course = await CreateCourseAsync(dbContext, profile.Id, category.Id, DeliveryFormat.Live);

        var start = DateTime.UtcNow.AddDays(2);
        var end = start.AddHours(2);
        var command = new CreateLiveSessionCommand("เซสชั่นแรก Kickoff", "รายละเอียดคอร์ส", start, end);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/catalog/instructor/courses/{course.Id}/live-sessions")
        {
            Content = JsonContent.Create(command, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var body = await response.Content.ReadFromJsonAsync<LiveSessionResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal("เซสชั่นแรก Kickoff", body.Title);
        Assert.Equal(CourseLiveSessionStatus.Scheduled, body.Status);
        Assert.Equal(0, body.SortOrder);
    }

    [Fact]
    public async Task CreateLiveSession_OnDemandCourse_Returns409Conflict()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (profile, token) = await CreateApprovedInstructorAndLoginAsync(_app.Services, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var course = await CreateCourseAsync(dbContext, profile.Id, category.Id, DeliveryFormat.OnDemand);

        var start = DateTime.UtcNow.AddDays(2);
        var end = start.AddHours(2);
        var command = new CreateLiveSessionCommand("Session", null, start, end);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/catalog/instructor/courses/{course.Id}/live-sessions")
        {
            Content = JsonContent.Create(command, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateLiveSession_NonOwner_Returns403Forbidden()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();

        var (ownerProfile, _) = await CreateApprovedInstructorAndLoginAsync(_app.Services, dbContext);
        var (_, otherToken) = await CreateApprovedInstructorAndLoginAsync(_app.Services, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var course = await CreateCourseAsync(dbContext, ownerProfile.Id, category.Id, DeliveryFormat.Live);

        var start = DateTime.UtcNow.AddDays(2);
        var end = start.AddHours(2);
        var command = new CreateLiveSessionCommand("Session", null, start, end);

        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/catalog/instructor/courses/{course.Id}/live-sessions")
        {
            Content = JsonContent.Create(command, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", otherToken);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task CreateLiveSession_OverlappingTime_Returns409Conflict()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var (profile, token) = await CreateApprovedInstructorAndLoginAsync(_app.Services, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var course = await CreateCourseAsync(dbContext, profile.Id, category.Id, DeliveryFormat.Live);

        var baseStart = DateTime.UtcNow.AddDays(3);
        course.AddLiveSession("Session 1", null, baseStart, baseStart.AddHours(2), clock);
        await dbContext.SaveChangesAsync();

        // Attempt overlap
        var overlapCommand = new CreateLiveSessionCommand("Session 2 Overlap", null, baseStart.AddMinutes(30), baseStart.AddHours(3));
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/catalog/instructor/courses/{course.Id}/live-sessions")
        {
            Content = JsonContent.Create(overlapCommand, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task CreateLiveSession_ContiguousBackToBack_Returns201Created()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var (profile, token) = await CreateApprovedInstructorAndLoginAsync(_app.Services, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var course = await CreateCourseAsync(dbContext, profile.Id, category.Id, DeliveryFormat.Live);

        // timestamptz(3): a start with finer ticks would be stored rounded, and "exactly at the previous end" would then fall a hair
        // inside it. Use whole seconds so what the request sends is exactly what the database holds.
        var baseStart = DateTime.UtcNow.AddDays(4);
        baseStart = new DateTime(baseStart.Ticks - baseStart.Ticks % TimeSpan.TicksPerSecond, DateTimeKind.Utc);
        var firstEnd = baseStart.AddHours(2);
        course.AddLiveSession("Session 1", null, baseStart, firstEnd, clock);
        await dbContext.SaveChangesAsync();

        // Contiguous: starts exactly at firstEnd
        var contiguousCommand = new CreateLiveSessionCommand("Session 2 Contiguous", null, firstEnd, firstEnd.AddHours(1));
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/catalog/instructor/courses/{course.Id}/live-sessions")
        {
            Content = JsonContent.Create(contiguousCommand, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    // ---- P11-02: Soft-Cancel & Delete Semantics ------------------------------------------------

    [Fact]
    public async Task DeleteLiveSession_SoftCancels_RowPreservedInDatabase()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var (profile, token) = await CreateApprovedInstructorAndLoginAsync(_app.Services, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var course = await CreateCourseAsync(dbContext, profile.Id, category.Id, DeliveryFormat.Live);

        var session = course.AddLiveSession("To Delete", null, DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(2).AddHours(1), clock);
        await dbContext.SaveChangesAsync();

        var request = new HttpRequestMessage(HttpMethod.Delete, $"/api/catalog/instructor/courses/{course.Id}/live-sessions/{session.Id}");
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        // Verify row is preserved in DB with Cancelled status
        await using var verifyScope = _app.Services.CreateAsyncScope();
        var verifyDb = verifyScope.ServiceProvider.GetRequiredService<AppDbContext>();
        var dbSession = await verifyDb.CourseLiveSessions().FirstOrDefaultAsync(s => s.Id == session.Id);
        Assert.NotNull(dbSession);
        Assert.Equal(CourseLiveSessionStatus.Cancelled, dbSession.Status);
        Assert.Null(dbSession.CancelReason);
    }

    [Fact]
    public async Task CancelLiveSession_ViaPost_SoftCancelsWithReason()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var (profile, token) = await CreateApprovedInstructorAndLoginAsync(_app.Services, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var course = await CreateCourseAsync(dbContext, profile.Id, category.Id, DeliveryFormat.Live);

        var session = course.AddLiveSession("To Cancel", null, DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(2).AddHours(1), clock);
        await dbContext.SaveChangesAsync();

        var cancelCmd = new CancelLiveSessionCommand("ผู้สอนติดภารกิจด่วน");
        var request = new HttpRequestMessage(HttpMethod.Post, $"/api/catalog/instructor/courses/{course.Id}/live-sessions/{session.Id}/cancel")
        {
            Content = JsonContent.Create(cancelCmd, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<LiveSessionResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.Equal(CourseLiveSessionStatus.Cancelled, body.Status);
        Assert.Equal("ผู้สอนติดภารกิจด่วน", body.CancelReason);
    }

    // ---- P11-02: Set Delivery Format Transitions ----------------------------------------------

    [Fact]
    public async Task SetDeliveryFormat_ToOnDemandWithScheduledSession_Returns409()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var (profile, token) = await CreateApprovedInstructorAndLoginAsync(_app.Services, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var course = await CreateCourseAsync(dbContext, profile.Id, category.Id, DeliveryFormat.Live);

        course.AddLiveSession("Session", null, DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(2).AddHours(1), clock);
        await dbContext.SaveChangesAsync();

        var command = new SetCourseDeliveryFormatCommand(DeliveryFormat.OnDemand);
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/catalog/instructor/courses/{course.Id}/delivery-format")
        {
            Content = JsonContent.Create(command, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    [Fact]
    public async Task SetDeliveryFormat_ToOnDemandWhenAllSessionsCancelled_Succeeds()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var (profile, token) = await CreateApprovedInstructorAndLoginAsync(_app.Services, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var course = await CreateCourseAsync(dbContext, profile.Id, category.Id, DeliveryFormat.Live);

        var session = course.AddLiveSession("Session", null, DateTime.UtcNow.AddDays(2), DateTime.UtcNow.AddDays(2).AddHours(1), clock);
        course.CancelLiveSession(session.Id, "reason", clock);
        await dbContext.SaveChangesAsync();

        var command = new SetCourseDeliveryFormatCommand(DeliveryFormat.OnDemand);
        var request = new HttpRequestMessage(HttpMethod.Put, $"/api/catalog/instructor/courses/{course.Id}/delivery-format")
        {
            Content = JsonContent.Create(command, options: JsonOptions),
        };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    // ---- P11-07: Public Course Detail and Search (Security Zero MeetUrl) ----------------------

    [Fact]
    public async Task GetCourseDetail_PublicEndpoint_ReturnsZeroMeetUrlExposure()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var (profile, _) = await CreateApprovedInstructorAndLoginAsync(_app.Services, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var course = await CreateCourseAsync(dbContext, profile.Id, category.Id, DeliveryFormat.Live);

        course.AddLiveSession("Live 1", null, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(2), clock);
        var section = course.AddSection("Section 1");
        var ep = section.AddEpisode("Ep 1", null, isFreePreview: true);
        ep.AttachMedia(Guid.NewGuid(), 600);
        course.Publish(clock);
        await dbContext.SaveChangesAsync();

        var response = await _client.GetAsync($"/api/catalog/courses/{course.Slug}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rawJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(rawJson);

        var hits = FindForbiddenMeetUrlProperties(doc.RootElement);
        Assert.Empty(hits);

        var detail = JsonSerializer.Deserialize<CourseDetailResponse>(rawJson, JsonOptions);
        Assert.NotNull(detail);
        Assert.NotNull(detail.LiveSchedule);
        Assert.Equal("Asia/Bangkok", detail.LiveSchedule.Timezone);
        Assert.Equal(1, detail.LiveSchedule.UpcomingCount);
        Assert.Single(detail.LiveSchedule.Sessions);
    }

    [Fact]
    public async Task SearchCourses_PublicEndpoint_ReturnsZeroMeetUrlExposureAndFiltersFormat()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var (profile, _) = await CreateApprovedInstructorAndLoginAsync(_app.Services, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var course = await CreateCourseAsync(dbContext, profile.Id, category.Id, DeliveryFormat.Live);

        course.AddLiveSession("Live 1", null, DateTime.UtcNow.AddDays(1), DateTime.UtcNow.AddDays(1).AddHours(2), clock);
        var section = course.AddSection("Section 1");
        var ep = section.AddEpisode("Ep 1", null, isFreePreview: true);
        ep.AttachMedia(Guid.NewGuid(), 600);
        course.Publish(clock);
        await dbContext.SaveChangesAsync();

        var response = await _client.GetAsync("/api/catalog/courses/search?format=Live");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var rawJson = await response.Content.ReadAsStringAsync();
        using var doc = JsonDocument.Parse(rawJson);

        var hits = FindForbiddenMeetUrlProperties(doc.RootElement);
        Assert.Empty(hits);

        var searchResult = JsonSerializer.Deserialize<SearchCoursesResponse>(rawJson, JsonOptions);
        Assert.NotNull(searchResult);
        Assert.Contains(searchResult.Results.Items, item => item.Id == course.Id && item.DeliveryFormat == DeliveryFormat.Live);
    }

    // ---- P11-01/P11-02/P11-07 contract §5: output-cache eviction (integrator-qa review gap fill) -----

    [Fact]
    public async Task CreateLiveSession_OnPublishedCourse_EvictsOutputCacheForCourseDetail()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var (profile, token) = await CreateApprovedInstructorAndLoginAsync(_app.Services, dbContext);
        var category = await CreateCategoryAsync(dbContext);
        var course = await CreateCourseAsync(dbContext, profile.Id, category.Id, DeliveryFormat.Live);

        var section = course.AddSection("Section 1");
        var ep = section.AddEpisode("Ep 1", null, isFreePreview: true);
        ep.AttachMedia(Guid.NewGuid(), 600);
        course.Publish(clock);
        await dbContext.SaveChangesAsync();

        // Populate the output cache with the pre-session state — same black-box technique
        // CourseReadModelTests.cs's SearchThenApprove test uses: drive the actual observable HTTP
        // behavior rather than inspecting IOutputCacheStore's internal, uninspectable in-memory state.
        var beforeResponse = await _client.GetAsync($"/api/catalog/courses/{course.Slug}");
        Assert.Equal(HttpStatusCode.OK, beforeResponse.StatusCode);
        var before = await beforeResponse.Content.ReadFromJsonAsync<CourseDetailResponse>(JsonOptions);
        Assert.NotNull(before);
        Assert.NotNull(before.LiveSchedule);
        Assert.Empty(before.LiveSchedule.Sessions);

        var start = DateTime.UtcNow.AddDays(3);
        var end = start.AddHours(1);
        var createRequest = new HttpRequestMessage(HttpMethod.Post, $"/api/catalog/instructor/courses/{course.Id}/live-sessions")
        {
            Content = JsonContent.Create(new CreateLiveSessionCommand("New Session", null, start, end), options: JsonOptions),
        };
        createRequest.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        var createResponse = await _client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        // If CreateLiveSessionHandler's EvictByTagAsync(CourseOutputCache.Tag, ...) call were missing
        // or wrong, this GET would still return the cached "no sessions" response captured above and
        // this assertion would fail — exactly contract P11-01 §5's "publish คอร์ส Live → เพิ่ม session
        // ใหม่ → GET courses/{slug} เห็น session ใหม่ทันที" scenario.
        var afterResponse = await _client.GetAsync($"/api/catalog/courses/{course.Slug}");
        Assert.Equal(HttpStatusCode.OK, afterResponse.StatusCode);
        var after = await afterResponse.Content.ReadFromJsonAsync<CourseDetailResponse>(JsonOptions);
        Assert.NotNull(after);
        Assert.NotNull(after.LiveSchedule);
        Assert.Single(after.LiveSchedule.Sessions);
        Assert.Equal("New Session", after.LiveSchedule.Sessions[0].Title);
    }

    private static List<string> FindForbiddenMeetUrlProperties(JsonElement element, string currentPath = "$")
    {
        var forbiddenSubstrings = new[] { "meeturl", "meetingurl", "meet_url" };
        var hits = new List<string>();

        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var prop in element.EnumerateObject())
                {
                    var propPath = $"{currentPath}.{prop.Name}";
                    var lower = prop.Name.ToLowerInvariant();
                    foreach (var forbidden in forbiddenSubstrings)
                    {
                        if (lower.Contains(forbidden))
                        {
                            hits.Add($"Forbidden property found: {propPath}");
                        }
                    }
                    hits.AddRange(FindForbiddenMeetUrlProperties(prop.Value, propPath));
                }
                break;
            case JsonValueKind.Array:
                var i = 0;
                foreach (var item in element.EnumerateArray())
                {
                    hits.AddRange(FindForbiddenMeetUrlProperties(item, $"{currentPath}[{i}]"));
                    i++;
                }
                break;
        }

        return hits;
    }
}
