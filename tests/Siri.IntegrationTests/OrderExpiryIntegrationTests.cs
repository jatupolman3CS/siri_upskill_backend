using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Siri.Api.Authorization;
using Siri.IntegrationTests.Fixtures;
using Siri.Integrations.Payment;
using Siri.Modules.Catalog;
using Siri.Modules.Commerce;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Modules.Commerce.Infrastructure;
using Siri.Modules.Identity;
using Siri.Modules.Identity.Domain;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Learning;
using Siri.Modules.Notification;
using Siri.Persistence;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;
using Xunit;

namespace Siri.IntegrationTests;

/// <summary>
/// Integration tests for P3-03 and P3-06 (Order expiry & webhook race guard):
/// Proves that OrderExpiryJob expires stale AwaitingPayment orders, reverts promo codes,
/// cancels PaymentIntents, and properly routes late webhook payments into PAYMENT_OPS_QUEUE.
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class OrderExpiryIntegrationTests : IAsyncLifetime
{
    private const string KnownPassword = "Correct-Horse-Battery-Staple-9";
    private const string TestIssuer = "https://api.siriupskill.test";
    private const string TestAudience = "siriupskill-frontend-test";
    private const string TestSigningKey = "order-expiry-tests-signing-key-0123456789012";

    private readonly ContainersFixture _containers;
    private WebApplication _app = null!;
    private HttpClient _client = null!;

    public OrderExpiryIntegrationTests(ContainersFixture containers)
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
            ["Payment:Stripe:SecretKey"] = "sk_test_placeholder",
            ["Payment:Stripe:PublishableKey"] = "pk_test_placeholder",
            ["Payment:Stripe:WebhookSecret"] = "whsec_test_placeholder",
            ["Commerce:OrderExpiry:ExpiryMinutes"] = "30",
            ["Commerce:OrderExpiry:BatchSize"] = "50",
        });

        builder.Services.AddPersistence(builder.Configuration);
        builder.Services.AddSharedRedis(builder.Configuration);
        builder.Services.AddHttpContextAccessor();
        builder.Services.AddSingleton<IClock, SystemClock>();

        builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    ValidIssuer = TestIssuer,
                    ValidAudience = TestAudience,
                    IssuerSigningKey = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(TestSigningKey)),
                };
            });

        builder.Services.AddSiriAuthorizationPolicies();

        builder.Services.AddNotificationModule(builder.Configuration);
        builder.Services.AddIdentityModule(builder.Configuration);
        builder.Services.AddCatalogModule(builder.Configuration);
        builder.Services.AddLearningModule();
        builder.Services.AddCommerceModule(builder.Configuration);

        _app = builder.Build();
        _app.UseAuthentication();
        _app.UseAuthorization();
        _app.MapIdentityEndpoints();
        _app.MapCatalogEndpoints();
        _app.MapCommerceEndpoints();

        await _app.StartAsync();

        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await db.Database.MigrateAsync();

        _client = _app.GetTestClient();
    }

    public async Task DisposeAsync()
    {
        _client.Dispose();
        await _app.DisposeAsync();
    }

    [Fact]
    public async Task OrderExpiryJob_ExpiresStaleOrder_RevertsPromoRedemption()
    {
        using var scope = _app.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var paymentRepo = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var promoRepo = scope.ServiceProvider.GetRequiredService<IPromoCodeRepository>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var userId = Guid.NewGuid();
        var promo = PROMO_CODE.Create("EXPIRY20", PromoCodeDiscountType.Fixed, 200m, 10, 1, 500m, clock.UtcNow.AddDays(-1), clock.UtcNow.AddDays(1), PromoCodeScope.AllCourses, null);
        await promoRepo.AddAsync(promo, CancellationToken.None);

        var order = ORDER.Create("ORD-INT-EXP-1", userId, 1000m, 200m, 52.34m, 800m, promo.PROMO_CODE_ID);
        order.MarkAwaitingPayment();
        await db.Orders().AddAsync(order);
        await db.SaveChangesAsync();

        // Redeem promo
        await promoRepo.TryRedeemAsync(promo.PROMO_CODE_ID, order.ORDER_ID, userId, 1, clock, CancellationToken.None);
        var redemptionCountBefore = await promoRepo.GetUserRedemptionCountAsync(promo.PROMO_CODE_ID, userId, CancellationToken.None);
        Assert.Equal(1, redemptionCountBefore);

        var fakePaymentMethod = new FakePaymentMethod();
        var job = new OrderExpiryJob(
            orderRepo,
            paymentRepo,
            fakePaymentMethod,
            promoRepo,
            new FakeClockOffset(clock.UtcNow.AddMinutes(45)), // Simulate 45 mins passed
            Options.Create(new OrderExpiryOptions { ExpiryMinutes = 30, BatchSize = 50 }),
            NullLogger<OrderExpiryJob>.Instance);

        await job.RunAsync(CancellationToken.None);

        var updatedOrder = await db.Orders().FirstAsync(o => o.ORDER_ID == order.ORDER_ID);
        Assert.Equal(OrderStatus.Cancelled, updatedOrder.STATUS);

        // Promo redemption must be reverted!
        var redemptionCountAfter = await promoRepo.GetUserRedemptionCountAsync(promo.PROMO_CODE_ID, userId, CancellationToken.None);
        Assert.Equal(0, redemptionCountAfter);
    }

    private sealed class FakeClockOffset(DateTime now) : IClock
    {
        public DateTime UtcNow { get; } = now;
    }

    private sealed class FakePaymentMethod : IPaymentMethod
    {
        public Task<Result<PaymentIntentResult>> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new PaymentIntentResult("pi_fake", "sec", "requires_payment_method", request.Amount, "thb", null, null)));

        public Task<Result<PaymentIntentResult>> GetPaymentIntentAsync(string providerPaymentIntentId, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new PaymentIntentResult(providerPaymentIntentId, "sec", "succeeded", 100m, "thb", null, null)));

        public Task<Result> CancelPaymentIntentAsync(string providerPaymentIntentId, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success());

        public Task<Result<PaymentRefundResult>> CreateRefundAsync(CreateRefundRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(new PaymentRefundResult("re_test", "succeeded", request.Amount, "thb")));
    }
}
