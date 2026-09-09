using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.IntegrationTests.Fixtures;
using Siri.Integrations.Payment.Stripe;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Modules.Catalog.Infrastructure.Contracts;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Modules.Commerce.Infrastructure;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Learning.Domain;
using Siri.Modules.Learning.Infrastructure;
using Siri.Modules.Learning.Infrastructure.Contracts;
using Siri.Modules.Notification.Contracts;
using Siri.Modules.Notification.Infrastructure;
using Siri.Modules.Payout;
using Siri.Modules.Payout.Contracts;
using Siri.Modules.Payout.Infrastructure;
using Siri.Modules.Payout.Infrastructure.Contracts;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.IntegrationTests;

public sealed class PaymentFulfillmentIntegrationTests(PostgresFixture fixture) : IClassFixture<PostgresFixture>
{
    private const string WebhookSecret = "whsec_fulfillment_integration_test";
    private readonly IClock _clock = new SystemClock();

    [Fact]
    public async Task PaidWebhook_WhenDownstreamSaveFails_RollsBackAndCanRetryExactlyOnce()
    {
        var userId = Guid.NewGuid();
        var paymentIntentId = $"pi_{Guid.NewGuid():N}";
        var eventId = $"evt_{Guid.NewGuid():N}";
        Guid orderId;
        Guid courseId;
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = await SeedCourseAsync(db);
            courseId = course.Id;
            var order = ORDER.Create($"SU-{Guid.NewGuid():N}"[..20], userId, 1000m, 0m, 0m, 1000m, null);
            order.AddItem(courseId, course.Title, 1000m, 1000m);
            order.MarkAwaitingPayment();
            db.Orders().Add(order);
            await db.SaveChangesAsync();
            orderId = order.ORDER_ID;
            await new PaymentRepository(db).AddAsync(
                PAYMENT.Create(orderId, PaymentMethod.PromptPay, paymentIntentId, 1000m, _clock), CancellationToken.None);
        }

        var json = JsonSerializer.Serialize(new
        {
            id = eventId, @object = "event", type = "payment_intent.succeeded",
            data = new { @object = new { id = paymentIntentId, @object = "payment_intent", amount = 100000, currency = "thb", status = "succeeded" } },
        });

        using (var scope = fixture.CreateScope())
        {
            var handler = CreateHandler(scope, failAfterRevenueSave: true);
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.HandleAsync(json, Sign(json), CancellationToken.None));
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(OrderStatus.AwaitingPayment, (await db.Orders().SingleAsync(o => o.ORDER_ID == orderId)).STATUS);
            Assert.Equal(PaymentStatus.Pending, (await db.Payments().SingleAsync(p => p.ORDER_ID == orderId)).STATUS);
            Assert.False(await db.Enrollments().AnyAsync(e => e.USER_ID == userId));
            Assert.False(await db.RevenueSplits().AnyAsync(s => db.OrderItems().Any(i => i.ORDER_ID == orderId && i.ORDER_ITEM_ID == s.ORDER_ITEM_ID)));
            Assert.False(await db.StripeWebhookEvents().AnyAsync(e => e.STRIPE_EVENT_ID == eventId));
        }

        for (var delivery = 0; delivery < 2; delivery++)
        {
            using var scope = fixture.CreateScope();
            var result = await CreateHandler(scope).HandleAsync(json, Sign(json), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var order = await db.Orders().SingleAsync(o => o.ORDER_ID == orderId);
            Assert.Equal(OrderStatus.Paid, order.STATUS);
            Assert.Equal(PaymentStatus.Succeeded, (await db.Payments().SingleAsync(p => p.ORDER_ID == orderId)).STATUS);
            var enrollment = await db.Enrollments().SingleAsync(e => e.USER_ID == userId && e.COURSE_ID == courseId);
            Assert.Equal(orderId, enrollment.ORDER_ID);
            Assert.NotNull(enrollment.EXPIRES_AT_UTC);
            Assert.Equal(1, await db.RevenueSplits().CountAsync(s => db.OrderItems().Any(i => i.ORDER_ID == orderId && i.ORDER_ITEM_ID == s.ORDER_ITEM_ID)));
            Assert.Equal(1, await db.StripeWebhookEvents().CountAsync(e => e.STRIPE_EVENT_ID == eventId));
            Assert.Equal(1, await db.EmailOutboxMessages().CountAsync(e => e.Subject.Contains(order.ORDER_NO)));
        }
    }

    [Fact]
    public async Task FreeOrder_WhenEnrollmentReturnsFailure_RollsBackOrderPromoAndAccess()
    {
        var userId = Guid.NewGuid();
        Guid courseId;
        Guid promoId;
        var promoCode = $"FREE{Guid.NewGuid():N}"[..32].ToUpperInvariant();
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            courseId = (await SeedCourseAsync(db)).Id;
            var promo = PROMO_CODE.Create(promoCode, PromoCodeDiscountType.Percentage, 100m, 10, 1, 0m,
                _clock.UtcNow.AddDays(-1), _clock.UtcNow.AddDays(1), PromoCodeScope.AllCourses, null);
            promoId = promo.PROMO_CODE_ID;
            await new PromoCodeRepository(db).AddAsync(promo, CancellationToken.None);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var catalog = new CatalogPriceContract(db);
            var promoRepository = new PromoCodeRepository(db);
            var learning = new LearningAccessContract(new EnrollmentRepository(db), catalog, _clock);
            var service = new OrderService(new OrderRepository(db), promoRepository, catalog,
                new FailingEnrollmentContract(learning),
                new PricingEngine(catalog, new FlashSaleRepository(db), new BundleRepository(db), promoRepository, _clock), _clock);

            var result = await service.CreateAsync(userId, new CreateOrderCommand([courseId], promoCode), CancellationToken.None);
            Assert.True(result.IsFailure);
            Assert.Equal("fulfillment_failed", result.Error.Code);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.False(await db.Orders().AnyAsync(o => o.USER_ID == userId));
            Assert.False(await db.Enrollments().AnyAsync(e => e.USER_ID == userId));
            Assert.False(await db.PromoRedemptions().AnyAsync(r => r.USER_ID == userId));
            Assert.Equal(0, (await db.PromoCodes().SingleAsync(p => p.PROMO_CODE_ID == promoId)).REDEEMED_COUNT);
        }
    }

    [Fact]
    public async Task MyEnrollments_IncludesMetadataForArchivedCourseAndPreservesMissingCourses()
    {
        var userId = Guid.NewGuid();
        using var scope = fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var course = await SeedCourseAsync(db);
        course.Archive();
        db.Enrollments().Add(ENROLLMENT.Create(userId, course.Id, null, EnrollmentSource.Purchase, null, _clock));
        db.Enrollments().Add(ENROLLMENT.Create(userId, Guid.NewGuid(), null, EnrollmentSource.Purchase, null, _clock));
        await db.SaveChangesAsync();

        var service = new EnrollmentService(new EnrollmentRepository(db), new CertificateRepository(db), _clock, new CatalogPriceContract(db));
        var result = await service.ListForUserAsync(userId, 1, 100, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        var enriched = Assert.Single(result.Items, e => e.CourseId == course.Id);
        Assert.Equal(course.Slug, enriched.CourseSlug);
        Assert.Equal(course.Title, enriched.CourseTitle);
        Assert.Equal(course.ThumbnailUrl, enriched.CourseThumbnailUrl);
        Assert.Equal("Integration Instructor", enriched.InstructorName);
        Assert.Null(Assert.Single(result.Items, e => e.CourseId != course.Id).CourseSlug);
    }

    private async Task<COURSE> SeedCourseAsync(AppDbContext db)
    {
        var category = CATEGORY.Create($"category-{Guid.NewGuid():N}", "Integration", "Integration", null, null, 0);
        var instructor = INSTRUCTOR_PROFILE.Apply(Guid.NewGuid(), "Integration Instructor", null, "Integration tests");
        instructor.Approve(_clock);
        db.Categories().Add(category);
        db.InstructorProfiles().Add(instructor);
        var course = COURSE.Create($"course-{Guid.NewGuid():N}", "Fulfillment integration course", instructor.Id,
            category.Id, CourseLevel.Beginner, CourseLanguage.English, 1000m);
        course.SetThumbnail("https://example.test/course.png");
        course.SetAccessDuration(30);
        var section = course.AddSection("Introduction");
        var episode = course.AddEpisode(section.Id, "First lesson", null, false);
        course.AttachEpisodeMedia(episode.Id, Guid.NewGuid(), 120);
        course.Publish(_clock);
        db.Courses().Add(course);
        await db.SaveChangesAsync();
        return course;
    }

    private StripeWebhookHandler CreateHandler(IServiceScope scope, bool failAfterRevenueSave = false)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var catalog = new CatalogPriceContract(db);
        IRevenueSplitContract revenue = new RevenueSplitContract(new RevenueSplitRepository(db), catalog, Options.Create(new PayoutOptions()), _clock);
        if (failAfterRevenueSave) revenue = new FailingRevenueContract(revenue);
        return new StripeWebhookHandler(new StripeWebhookEventRepository(db), new PaymentRepository(db), new OrderRepository(db),
            new PromoCodeRepository(db), new PaymentOpsQueueRepository(db), catalog,
            new LearningAccessContract(new EnrollmentRepository(db), catalog, _clock), revenue,
            scope.ServiceProvider.GetRequiredService<IEmailOutbox>(), new UserContactReader(),
            Options.Create(new StripeOptions { WebhookSecret = WebhookSecret }), _clock, NullLogger<StripeWebhookHandler>.Instance);
    }

    private static string Sign(string json)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), Encoding.UTF8.GetBytes($"{timestamp}.{json}"));
        return $"t={timestamp},v1={Convert.ToHexStringLower(signature)}";
    }

    private sealed class UserContactReader : IUserContactReader
    {
        public Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken) => Task.FromResult<string?>("buyer@example.test");

        public Task<(string? Email, string? DisplayName)> GetUserContactInfoAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<(string?, string?)>(("buyer@example.test", "Buyer"));
    }

    private sealed class FailingRevenueContract(IRevenueSplitContract inner) : IRevenueSplitContract
    {
        public async Task RecordRevenueSplitsAsync(Guid orderId, IReadOnlyList<OrderItemSplitInfo> items, CancellationToken cancellationToken)
        {
            await inner.RecordRevenueSplitsAsync(orderId, items, cancellationToken);
            throw new InvalidOperationException("Simulated failure after revenue save");
        }

        public Task ReverseRevenueSplitsForOrderAsync(Guid orderId, IReadOnlyList<Guid> orderItemIds, CancellationToken cancellationToken) =>
            inner.ReverseRevenueSplitsForOrderAsync(orderId, orderItemIds, cancellationToken);
    }

    private sealed class FailingEnrollmentContract(ILearningAccessContract inner) : ILearningAccessContract
    {
        public Task<bool> CanUserAccessEpisodeAsync(Guid userId, Guid episodeId, CancellationToken cancellationToken) =>
            inner.CanUserAccessEpisodeAsync(userId, episodeId, cancellationToken);

        public Task<bool> HasActiveEnrollmentAsync(Guid userId, Guid courseId, CancellationToken cancellationToken) =>
            inner.HasActiveEnrollmentAsync(userId, courseId, cancellationToken);

        public async Task<Result> EnrollUserAsync(Guid userId, Guid courseId, Guid? orderId, string source, DateTime? expiresAtUtc, CancellationToken cancellationToken)
        {
            await inner.EnrollUserAsync(userId, courseId, orderId, source, expiresAtUtc, cancellationToken);
            return Result.Failure(new DomainError("fulfillment_failed", "Simulated enrollment failure"));
        }
    }
}
