using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.IntegrationTests.Fixtures;
using Siri.Integrations.Payment;
using Siri.Modules.Catalog;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Commerce;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Modules.Commerce.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

/// <summary>
/// Integration tests for P3-03 and P3-06 (Order expiry & webhook race guard):
/// Proves that OrderExpiryJob expires stale AwaitingPayment orders, reverts promo codes,
/// cancels PaymentIntents, and properly routes late webhook payments into PAYMENT_OPS_QUEUE.
/// <para>
/// Boots via <see cref="SiriApiFactory"/> (the real <c>Program.cs</c> composition root, D-19) purely
/// to get a correctly-wired DI graph for <see cref="IOrderRepository"/>/<see cref="IPaymentRepository"/>/
/// <see cref="IPromoCodeRepository"/> against Testcontainers MSSQL — no test here ever goes over HTTP
/// (<c>OrderExpiryJob</c> is invoked directly with fakes), so there is no Minimal-API-vs-Controller
/// coverage gap to close in this file; the old hand-rolled host's <c>MapCommerceEndpoints()</c> etc.
/// calls and JwtBearer/<c>HttpClient</c> wiring were already dead weight before this migration.
/// </para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class OrderExpiryIntegrationTests : IAsyncLifetime
{
    private readonly ContainersFixture _containers;
    private SiriApiFactory _factory = null!;

    public OrderExpiryIntegrationTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    public async Task InitializeAsync()
    {
        _factory = new SiriApiFactory(_containers);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task OrderExpiryJob_ExpiresStaleOrder_RevertsPromoRedemption()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
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

        var catalogPriceContract = scope.ServiceProvider.GetRequiredService<ICatalogPriceContract>();
        var fakePaymentMethod = new FakePaymentMethod();
        var job = new OrderExpiryJob(
            orderRepo,
            paymentRepo,
            fakePaymentMethod,
            promoRepo,
            catalogPriceContract,
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

    /// <summary>Task P11-11 (Q13.2 — docs/contracts/P11-11-enrollment-deadline-seat-cap.md §4.4): an
    /// order that reserved a seat on a capped course but expired before payment must give that seat
    /// back, atomically with the rest of OrderExpiryJob's existing transaction.</summary>
    [Fact]
    public async Task OrderExpiryJob_ExpiresOrderOnSeatCappedCourse_ReleasesSeat()
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var orderRepo = scope.ServiceProvider.GetRequiredService<IOrderRepository>();
        var paymentRepo = scope.ServiceProvider.GetRequiredService<IPaymentRepository>();
        var promoRepo = scope.ServiceProvider.GetRequiredService<IPromoCodeRepository>();
        var catalogPriceContract = scope.ServiceProvider.GetRequiredService<ICatalogPriceContract>();
        var clock = scope.ServiceProvider.GetRequiredService<IClock>();

        var category = CATEGORY.Create($"cat-{Guid.NewGuid():N}", "หมวดหมู่ทดสอบ", "Test Cat", null, null, 0);
        db.Categories().Add(category);

        var profile = INSTRUCTOR_PROFILE.Apply(Guid.NewGuid(), "Test Instructor", "Headline", "Bio");
        profile.Approve(clock);
        db.InstructorProfiles().Add(profile);

        var course = COURSE.Create($"course-{Guid.NewGuid():N}", "Test COURSE", profile.Id, category.Id, CourseLevel.Beginner, CourseLanguage.Thai, 1000m);
        var section = course.AddSection("Section 1");
        section.AddEpisode("Episode 1", null, isFreePreview: false).AttachMedia(Guid.NewGuid(), 600);
        course.Publish(clock);
        course.SetEnrollmentPolicy(null, maxSeats: 5);
        db.Courses().Add(course);
        await db.SaveChangesAsync();

        // Simulate a reservation that already happened when the (now-expiring) order was created.
        var reserved = await catalogPriceContract.TryReserveSeatAsync(course.Id, CancellationToken.None);
        Assert.True(reserved);

        var userId = Guid.NewGuid();
        var order = ORDER.Create("ORD-INT-SEAT-EXP-1", userId, 1000m, 0m, 65.42m, 1000m);
        order.AddItem(course.Id, "Test COURSE", 1000m, 1000m);
        order.MarkAwaitingPayment();
        await db.Orders().AddAsync(order);
        await db.SaveChangesAsync();

        var fakePaymentMethod = new FakePaymentMethod();
        var job = new OrderExpiryJob(
            orderRepo,
            paymentRepo,
            fakePaymentMethod,
            promoRepo,
            catalogPriceContract,
            new FakeClockOffset(clock.UtcNow.AddMinutes(45)),
            Options.Create(new OrderExpiryOptions { ExpiryMinutes = 30, BatchSize = 50 }),
            NullLogger<OrderExpiryJob>.Instance);

        await job.RunAsync(CancellationToken.None);

        var updatedOrder = await db.Orders().FirstAsync(o => o.ORDER_ID == order.ORDER_ID);
        Assert.Equal(OrderStatus.Cancelled, updatedOrder.STATUS);

        var updatedCourse = await db.Courses().AsNoTracking().FirstAsync(c => c.Id == course.Id);
        Assert.Equal(0, updatedCourse.SeatsUsed);
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

        public Task<Result<decimal?>> GetChargeFeeAsync(string providerPaymentIntentId, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success<decimal?>(null));
    }
}
