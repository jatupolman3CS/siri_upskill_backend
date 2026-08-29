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
/// Integration tests for P3-09 Promo Code:
/// Validation, Order discount calculation, PROMO_REDEMPTION persistence, and atomic SQL race-condition prevention.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class PromoCodeTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "promo-code-tests-signing-key-0123456789012";

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() },
    };

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public PromoCodeTests(ContainersFixture containers)
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

    private async Task<(USER USER, string Token)> CreateUserAndLoginAsync(IServiceProvider services, AppDbContext dbContext)
    {
        var email = $"user-{Guid.NewGuid():N}@example.test";
        var user = await CreateUserAsync(services, dbContext, email, KnownPassword);
        var token = await LoginAndGetAccessTokenAsync(services, email);
        return (user, token);
    }

    private static async Task<COURSE> CreatePublishedCourseAsync(
        IServiceProvider services, AppDbContext dbContext, decimal price = 1000m)
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
    public async Task ValidatePromoCode_ValidCode_ReturnsDiscountCalculation()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, token) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);
        var course = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, 1000m);

        var codeName = $"SAVE200_{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var now = DateTime.UtcNow;
        var promo = PROMO_CODE.Create(codeName, PromoCodeDiscountType.Fixed, 200m, 10, 1, 500m, now.AddDays(-1), now.AddDays(1), PromoCodeScope.AllCourses, null);
        dbContext.PromoCodes().Add(promo);
        await dbContext.SaveChangesAsync();

        using var request = AuthenticatedRequest(HttpMethod.Post, "/api/commerce/promo-codes/validate", token);
        request.Content = JsonContent.Create(new ValidatePromoCodeCommand(codeName, [course.Id]));

        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var body = await response.Content.ReadFromJsonAsync<ValidatePromoCodeResponse>(JsonOptions);
        Assert.NotNull(body);
        Assert.True(body!.IsValid);
        Assert.Equal(1000m, body.Subtotal);
        Assert.Equal(200m, body.DiscountAmount);
        Assert.Equal(800m, body.TotalAmount);
    }

    [Fact]
    public async Task CreateOrder_WithValidPromoCode_CreatesOrderAndInsertsPromoRedemption()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (user, token) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);
        var course = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, 1000m);

        var codeName = $"ORDER200_{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var now = DateTime.UtcNow;
        var promo = PROMO_CODE.Create(codeName, PromoCodeDiscountType.Fixed, 200m, 10, 1, 500m, now.AddDays(-1), now.AddDays(1), PromoCodeScope.AllCourses, null);
        dbContext.PromoCodes().Add(promo);
        await dbContext.SaveChangesAsync();

        using var request = AuthenticatedRequest(HttpMethod.Post, "/api/commerce/orders", token);
        request.Content = JsonContent.Create(new CreateOrderCommand([course.Id], codeName));

        using var response = await _client.SendAsync(request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var orderRes = await response.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(orderRes);
        Assert.Equal(800m, orderRes!.TotalAmount);

        // Verify database state
        var orderDb = await dbContext.Orders().AsNoTracking().SingleAsync(o => o.ORDER_ID == orderRes.Id);
        Assert.Equal(1000m, orderDb.SUBTOTAL_AMOUNT);
        Assert.Equal(200m, orderDb.DISCOUNT_AMOUNT);
        Assert.Equal(800m, orderDb.TOTAL_AMOUNT);
        Assert.Equal(promo.PROMO_CODE_ID, orderDb.PROMO_CODE_ID);

        var redemptionDb = await dbContext.PromoRedemptions().AsNoTracking().SingleAsync(r => r.ORDER_ID == orderDb.ORDER_ID);
        Assert.Equal(promo.PROMO_CODE_ID, redemptionDb.PROMO_CODE_ID);
        Assert.Equal(user.Id, redemptionDb.USER_ID);

        var promoDb = await dbContext.PromoCodes().AsNoTracking().SingleAsync(p => p.PROMO_CODE_ID == promo.PROMO_CODE_ID);
        Assert.Equal(1, promoDb.REDEEMED_COUNT);
    }

    [Fact]
    public async Task CreateOrder_AtomicConcurrencyRace_OnlyOneSucceedsWhenQuotaIsOne()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (_, token1) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);
        var (_, token2) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);
        var course = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, 1000m);

        var codeName = $"RACE_{Guid.NewGuid():N}"[..10].ToUpperInvariant();
        var now = DateTime.UtcNow;
        // maxRedemptions = 1 (only 1 available slot)
        var promo = PROMO_CODE.Create(codeName, PromoCodeDiscountType.Fixed, 100m, 1, 1, 0m, now.AddDays(-1), now.AddDays(1), PromoCodeScope.AllCourses, null);
        dbContext.PromoCodes().Add(promo);
        await dbContext.SaveChangesAsync();

        // Launch 2 requests simultaneously
        var req1 = AuthenticatedRequest(HttpMethod.Post, "/api/commerce/orders", token1);
        req1.Content = JsonContent.Create(new CreateOrderCommand([course.Id], codeName));

        var req2 = AuthenticatedRequest(HttpMethod.Post, "/api/commerce/orders", token2);
        req2.Content = JsonContent.Create(new CreateOrderCommand([course.Id], codeName));

        var task1 = _client.SendAsync(req1);
        var task2 = _client.SendAsync(req2);

        var responses = await Task.WhenAll(task1, task2);

        var statusCodes = responses.Select(r => r.StatusCode).ToList();
        Assert.Contains(HttpStatusCode.Created, statusCodes);
        Assert.Contains(HttpStatusCode.Conflict, statusCodes);

        // Verify exactly 1 redemption in DB
        var promoDb = await dbContext.PromoCodes().AsNoTracking().SingleAsync(p => p.PROMO_CODE_ID == promo.PROMO_CODE_ID);
        Assert.Equal(1, promoDb.REDEEMED_COUNT);

        var redemptionsCount = await dbContext.PromoRedemptions().AsNoTracking().CountAsync(r => r.PROMO_CODE_ID == promo.PROMO_CODE_ID);
        Assert.Equal(1, redemptionsCount);
    }

    [Fact]
    public async Task CreateOrder_SingleUserConcurrencyRace_OnlyOneSucceedsWhenMaxPerUserIsOne()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (user, token) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);
        var course = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, 1000m);

        var codeName = $"USER_RACE_{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var now = DateTime.UtcNow;
        // maxRedemptions = 10 (plenty of global quota), but maxPerUser = 1
        var promo = PROMO_CODE.Create(codeName, PromoCodeDiscountType.Fixed, 100m, 10, 1, 0m, now.AddDays(-1), now.AddDays(1), PromoCodeScope.AllCourses, null);
        dbContext.PromoCodes().Add(promo);
        await dbContext.SaveChangesAsync();

        // Same user launches 2 requests simultaneously (e.g. rapid double click)
        var req1 = AuthenticatedRequest(HttpMethod.Post, "/api/commerce/orders", token);
        req1.Content = JsonContent.Create(new CreateOrderCommand([course.Id], codeName));

        var req2 = AuthenticatedRequest(HttpMethod.Post, "/api/commerce/orders", token);
        req2.Content = JsonContent.Create(new CreateOrderCommand([course.Id], codeName));

        var task1 = _client.SendAsync(req1);
        var task2 = _client.SendAsync(req2);

        var responses = await Task.WhenAll(task1, task2);

        var statusCodes = responses.Select(r => r.StatusCode).ToList();
        Assert.Contains(HttpStatusCode.Created, statusCodes);
        Assert.Contains(HttpStatusCode.Conflict, statusCodes);

        // Verify exactly 1 redemption in DB for this user
        var promoDb = await dbContext.PromoCodes().AsNoTracking().SingleAsync(p => p.PROMO_CODE_ID == promo.PROMO_CODE_ID);
        Assert.Equal(1, promoDb.REDEEMED_COUNT);

        var userRedemptionsCount = await dbContext.PromoRedemptions().AsNoTracking().CountAsync(r => r.PROMO_CODE_ID == promo.PROMO_CODE_ID && r.USER_ID == user.Id);
        Assert.Equal(1, userRedemptionsCount);
    }

    [Fact]
    public async Task CreateOrder_TransactionRollback_DoesNotLeakRedemptionOrDeductQuota()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var (user, token) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);
        var course = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, 1000m);

        var codeName = $"EXHAUST_{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var now = DateTime.UtcNow;
        // Quota is 0 (already fully redeemed)
        var promo = PROMO_CODE.Create(codeName, PromoCodeDiscountType.Fixed, 100m, 1, 1, 0m, now.AddDays(-1), now.AddDays(1), PromoCodeScope.AllCourses, null);
        dbContext.PromoCodes().Add(promo);
        await dbContext.SaveChangesAsync();

        // Artificially max out redeemed count in DB
        await dbContext.PromoCodes()
            .Where(p => p.PROMO_CODE_ID == promo.PROMO_CODE_ID)
            .ExecuteUpdateAsync(s => s.SetProperty(p => p.REDEEMED_COUNT, 1));

        var req = AuthenticatedRequest(HttpMethod.Post, "/api/commerce/orders", token);
        req.Content = JsonContent.Create(new CreateOrderCommand([course.Id], codeName));

        using var response = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // Verify rollback: no order created for user, no redemption inserted, count stayed at 1
        var userOrdersCount = await dbContext.Orders().AsNoTracking().CountAsync(o => o.USER_ID == user.Id);
        Assert.Equal(0, userOrdersCount);

        var redemptionsCount = await dbContext.PromoRedemptions().AsNoTracking().CountAsync(r => r.PROMO_CODE_ID == promo.PROMO_CODE_ID);
        Assert.Equal(0, redemptionsCount);
    }

    [Fact]
    public async Task Refund_WithPromoCode_RevertsPromoRedemptionAndQuota()
    {
        await using var scope = _app.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();
        var refundService = scope.ServiceProvider.GetRequiredService<RefundService>();

        var (user, token) = await CreateUserAndLoginAsync(scope.ServiceProvider, dbContext);
        var course = await CreatePublishedCourseAsync(scope.ServiceProvider, dbContext, 1000m);

        var codeName = $"REFUND_{Guid.NewGuid():N}"[..12].ToUpperInvariant();
        var now = DateTime.UtcNow;
        var promo = PROMO_CODE.Create(codeName, PromoCodeDiscountType.Fixed, 200m, 5, 1, 0m, now.AddDays(-1), now.AddDays(1), PromoCodeScope.AllCourses, null);
        dbContext.PromoCodes().Add(promo);
        await dbContext.SaveChangesAsync();

        // 1. Create order with promo code
        var req = AuthenticatedRequest(HttpMethod.Post, "/api/commerce/orders", token);
        req.Content = JsonContent.Create(new CreateOrderCommand([course.Id], codeName));
        using var orderRes = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Created, orderRes.StatusCode);
        var orderData = await orderRes.Content.ReadFromJsonAsync<OrderResponse>(JsonOptions);
        Assert.NotNull(orderData);

        // Verify redeemed count = 1
        var promoDbBefore = await dbContext.PromoCodes().AsNoTracking().SingleAsync(p => p.PROMO_CODE_ID == promo.PROMO_CODE_ID);
        Assert.Equal(1, promoDbBefore.REDEEMED_COUNT);
        Assert.Equal(1, await dbContext.PromoRedemptions().AsNoTracking().CountAsync(r => r.PROMO_CODE_ID == promo.PROMO_CODE_ID));

        // 2. Simulate payment succeeded
        var payment = PAYMENT.Create(orderData!.Id, PaymentMethod.PromptPay, $"pi_{Guid.NewGuid():N}", 800m, clock);
        payment.MarkSucceeded(clock);
        dbContext.Payments().Add(payment);
        await dbContext.SaveChangesAsync();

        // 3. USER requests refund and admin approves
        var reqRefundResult = await refundService.RequestAsync(user.Id, new RequestRefundCommand(payment.PAYMENT_ID, 800m, "Need refund"), CancellationToken.None);
        Assert.True(reqRefundResult.IsSuccess);

        var adminId = Guid.NewGuid();
        var approveResult = await refundService.ApproveAsync(adminId, reqRefundResult.Value.Id, new ApproveRefundCommand("Approved"), CancellationToken.None);
        Assert.True(approveResult.IsSuccess);

        // 4. Verify promo redemption is reverted and REDEEMED_COUNT decremented back to 0
        var promoDbAfter = await dbContext.PromoCodes().AsNoTracking().SingleAsync(p => p.PROMO_CODE_ID == promo.PROMO_CODE_ID);
        Assert.Equal(0, promoDbAfter.REDEEMED_COUNT);

        var redemptionsAfter = await dbContext.PromoRedemptions().AsNoTracking().CountAsync(r => r.PROMO_CODE_ID == promo.PROMO_CODE_ID);
        Assert.Equal(0, redemptionsAfter);
    }
}
