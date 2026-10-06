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
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Commerce;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Modules.Commerce.Infrastructure;
using Siri.Modules.Identity;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Features.Login;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Learning;
using Siri.Modules.Notification;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;
using Xunit;

namespace Siri.IntegrationTests;

/// <summary>
/// Integration tests for P11-11 (Q13.1/Q13.2 — docs/contracts/P11-11-enrollment-deadline-seat-cap.md):
/// enrollment-deadline enforcement and atomic seat-cap reservation/release in OrderService.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class SeatCapAndEnrollmentDeadlineTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "seat-cap-tests-signing-key-01234567890123";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public SeatCapAndEnrollmentDeadlineTests(ContainersFixture containers)
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
            ["Stripe:SecretKey"] = "sk_test_mock",
            ["Stripe:PublishableKey"] = "pk_test_mock",
            ["Stripe:WebhookSecret"] = "whsec_mock",
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
        builder.Services.AddCommerceModule(builder.Configuration);

        _app = builder.Build();

        _app.UseAuthentication();
        _app.UseAuthorization();

        _app.MapCatalogEndpoints();
        _app.MapCommerceEndpoints();

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

    private static async Task<(USER User, string Token)> CreateUserAndLoginAsync(IServiceProvider services, AppDbContext dbContext)
    {
        var email = $"user-{Guid.NewGuid():N}@example.test";
        var user = await CreateUserAsync(services, dbContext, email, KnownPassword);
        var token = await LoginAndGetAccessTokenAsync(services, email);
        return (user, token);
    }

    /// <summary>Mirrors PromoCodeTests.CreatePublishedCourseAsync, extended with the P11-11 enrollment
    /// policy fields — set through the same domain method (<c>COURSE.SetEnrollmentPolicy</c>) the new
    /// PUT .../enrollment-policy endpoint calls, not through the endpoint itself (no need to round-trip
    /// HTTP just to seed test data — same reasoning PromoCodeTests uses for seeding promo codes
    /// directly via <c>PROMO_CODE.Create</c>).</summary>
    private static async Task<COURSE> CreatePublishedCourseAsync(
        IServiceProvider services,
        AppDbContext dbContext,
        decimal price = 1000m,
        DateTime? enrollmentDeadlineUtc = null,
        int? maxSeats = null)
    {
        var clock = services.GetRequiredService<IClock>();

        var category = CATEGORY.Create($"cat-{Guid.NewGuid():N}", "หมวดหมู่ทดสอบ", "Test Cat", null, null, 0);
        dbContext.Categories().Add(category);

        var profile = INSTRUCTOR_PROFILE.Apply(Guid.NewGuid(), "Test Instructor", "Headline", "Bio");
        profile.Approve(clock);
        dbContext.InstructorProfiles().Add(profile);

        var course = COURSE.Create($"course-{Guid.NewGuid():N}", "Test COURSE", profile.Id, category.Id, CourseLevel.Beginner, CourseLanguage.Thai, price);
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);
        course.Publish(clock);

        if (enrollmentDeadlineUtc is not null || maxSeats is not null)
        {
            course.SetEnrollmentPolicy(enrollmentDeadlineUtc, maxSeats);
        }

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

    [Fact]
    public async Task CreateOrder_TwoBuyersRaceForLastSeat_OnlyOneSucceedsAndSeatsUsedEndsAtOne()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, token1) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);
        var (_, token2) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);
        var course = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, maxSeats: 1);

        var req1 = AuthenticatedRequest(HttpMethod.Post, "/api/commerce/orders", token1);
        req1.Content = JsonContent.Create(new CreateOrderCommand([course.Id]));

        var req2 = AuthenticatedRequest(HttpMethod.Post, "/api/commerce/orders", token2);
        req2.Content = JsonContent.Create(new CreateOrderCommand([course.Id]));

        var task1 = _client.SendAsync(req1);
        var task2 = _client.SendAsync(req2);

        var responses = await Task.WhenAll(task1, task2);

        var statusCodes = responses.Select(r => r.StatusCode).ToList();
        Assert.Contains(HttpStatusCode.Created, statusCodes);
        Assert.Contains(HttpStatusCode.Conflict, statusCodes);

        var courseDb = await dbContext.Courses().AsNoTracking().SingleAsync(c => c.Id == course.Id);
        Assert.Equal(1, courseDb.SeatsUsed);

        // Exactly one ORDER_ITEM referencing this course should exist — not zero, not two.
        var orderItemCount = await dbContext.OrderItems().AsNoTracking().CountAsync(i => i.COURSE_ID == course.Id);
        Assert.Equal(1, orderItemCount);
    }

    [Fact]
    public async Task CreateOrder_EnrollmentDeadlineHasPassed_ReturnsConflictAndCreatesNoOrder()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, token) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);
        var course = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, enrollmentDeadlineUtc: DateTime.UtcNow.AddDays(-1));

        var request = AuthenticatedRequest(HttpMethod.Post, "/api/commerce/orders", token);
        request.Content = JsonContent.Create(new CreateOrderCommand([course.Id]));

        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var orderCount = await dbContext.Orders().AsNoTracking().CountAsync();
        Assert.Equal(0, orderCount);
        var orderItemCount = await dbContext.OrderItems().AsNoTracking().CountAsync(i => i.COURSE_ID == course.Id);
        Assert.Equal(0, orderItemCount);
    }

    [Fact]
    public async Task CancelOrder_OnSeatCappedCourse_ReleasesSeatBackToPool()
    {
        // NOTE: this fixture boots the legacy Minimal API MapOrderEndpoints, not the production
        // OrdersController MVC controller that now exposes POST .../cancel (see
        // OrderCancellationIntegrationTests, which uses SiriApiFactory to go through the real
        // controller) — so this test still calls OrderService.CancelAsync directly, the same way
        // OrderExpiryIntegrationTests exercises OrderExpiryJob directly rather than through HTTP.
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orderService = scope.ServiceProvider.GetRequiredService<OrderService>();
        var (user, token) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);
        var course = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, maxSeats: 1);

        using var createRequest = AuthenticatedRequest(HttpMethod.Post, "/api/commerce/orders", token);
        createRequest.Content = JsonContent.Create(new CreateOrderCommand([course.Id]));
        using var createResponse = await _client.SendAsync(createRequest);
        Assert.Equal(HttpStatusCode.Created, createResponse.StatusCode);

        var order = await createResponse.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(order);

        var courseAfterCreate = await dbContext.Courses().AsNoTracking().SingleAsync(c => c.Id == course.Id);
        Assert.Equal(1, courseAfterCreate.SeatsUsed);

        var cancelResult = await orderService.CancelAsync(user.Id, order!.Id, CancellationToken.None);
        Assert.True(cancelResult.IsSuccess);

        var courseAfterCancel = await dbContext.Courses().AsNoTracking().SingleAsync(c => c.Id == course.Id);
        Assert.Equal(0, courseAfterCancel.SeatsUsed);

        // A second buyer must now be able to take the freed-up seat.
        var (_, secondToken) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);
        using var secondRequest = AuthenticatedRequest(HttpMethod.Post, "/api/commerce/orders", secondToken);
        secondRequest.Content = JsonContent.Create(new CreateOrderCommand([course.Id]));
        using var secondResponse = await _client.SendAsync(secondRequest);
        Assert.Equal(HttpStatusCode.Created, secondResponse.StatusCode);
    }

    [Fact]
    public async Task CreateOrder_CourseWithNoEnrollmentPolicySet_PurchasesNormallyAsBeforeThisTask()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, token) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);
        var course = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext);

        var request = AuthenticatedRequest(HttpMethod.Post, "/api/commerce/orders", token);
        request.Content = JsonContent.Create(new CreateOrderCommand([course.Id]));

        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var courseDb = await dbContext.Courses().AsNoTracking().SingleAsync(c => c.Id == course.Id);
        Assert.Equal(0, courseDb.SeatsUsed);
        Assert.Null(courseDb.MaxSeats);
        Assert.Null(courseDb.EnrollmentDeadlineUtc);
    }
}
