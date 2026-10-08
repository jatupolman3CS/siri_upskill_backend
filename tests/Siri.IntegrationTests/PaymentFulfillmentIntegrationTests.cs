using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.IntegrationTests.Fixtures;
using Siri.Integrations.Payment;
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

    /// <summary>The real Catalog updater + Learning counter — what DI wires in production (Learning reports enrollment transitions to it).</summary>
    private static Siri.Modules.Catalog.Application.CourseEnrollmentCountUpdater CountUpdater(AppDbContext db) => new(db, new LearningEnrollmentCounter(db));

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
            var learning = new LearningAccessContract(new EnrollmentRepository(db), catalog, _clock, CountUpdater(db));
            var service = new OrderService(new OrderRepository(db), promoRepository, catalog,
                new LiveScheduleReader(db),
                new FailingEnrollmentContract(learning),
                new PricingEngine(catalog, new FlashSaleRepository(db), new BundleRepository(db), promoRepository, _clock), _clock,
                new PaymentRepository(db), new StubPaymentMethod(null));

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

        var service = new EnrollmentService(new EnrollmentRepository(db), new CertificateRepository(db), _clock, new CatalogPriceContract(db), CountUpdater(db));
        var result = await service.ListForUserAsync(userId, 1, 100, CancellationToken.None);

        Assert.Equal(2, result.TotalCount);
        var enriched = Assert.Single(result.Items, e => e.CourseId == course.Id);
        Assert.Equal(course.Slug, enriched.CourseSlug);
        Assert.Equal(course.Title, enriched.CourseTitle);
        Assert.Equal(course.ThumbnailUrl, enriched.CourseThumbnailUrl);
        Assert.Equal("Integration Instructor", enriched.InstructorName);
        Assert.Null(Assert.Single(result.Items, e => e.CourseId != course.Id).CourseSlug);
    }

    [Fact]
    public async Task CardPayment_PendingConfirmation_DoesNotEnrollUntilWebhookSucceeds()
    {
        var userId = Guid.NewGuid();
        var paymentIntentId = $"pi_{Guid.NewGuid():N}";
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
            // Simulates a Card PaymentIntent stuck at requires_action (3DS not yet completed) —
            // PAYMENT row exists but no webhook event has been delivered for it yet.
            await new PaymentRepository(db).AddAsync(
                PAYMENT.Create(orderId, PaymentMethod.Card, paymentIntentId, 1000m, _clock), CancellationToken.None);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(OrderStatus.AwaitingPayment, (await db.Orders().SingleAsync(o => o.ORDER_ID == orderId)).STATUS);
            Assert.False(await db.Enrollments().AnyAsync(e => e.USER_ID == userId));
        }

        var eventId = $"evt_{Guid.NewGuid():N}";
        var json = JsonSerializer.Serialize(new
        {
            id = eventId, @object = "event", type = "payment_intent.succeeded",
            data = new { @object = new { id = paymentIntentId, @object = "payment_intent", amount = 100000, currency = "thb", status = "succeeded" } },
        });

        using (var scope = fixture.CreateScope())
        {
            var result = await CreateHandler(scope).HandleAsync(json, Sign(json), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(OrderStatus.Paid, (await db.Orders().SingleAsync(o => o.ORDER_ID == orderId)).STATUS);
            Assert.True(await db.Enrollments().AnyAsync(e => e.USER_ID == userId && e.COURSE_ID == courseId));
        }
    }

    [Fact]
    public async Task PaidWebhook_TwoItemOrder_WithChargeFee_AllocatesFeeProportionallyExactly()
    {
        var userId = Guid.NewGuid();
        var paymentIntentId = $"pi_{Guid.NewGuid():N}";
        var eventId = $"evt_{Guid.NewGuid():N}";
        Guid orderId, itemAId, itemBId;
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var courseA = await SeedCourseAsync(db);
            var courseB = await SeedCourseAsync(db);
            var order = ORDER.Create($"SU-{Guid.NewGuid():N}"[..20], userId, 1000m, 0m, 0m, 1000m, null);
            var itemA = order.AddItem(courseA.Id, courseA.Title, 700m, 700m);
            var itemB = order.AddItem(courseB.Id, courseB.Title, 300m, 300m);
            itemAId = itemA.ORDER_ITEM_ID;
            itemBId = itemB.ORDER_ITEM_ID;
            order.MarkAwaitingPayment();
            db.Orders().Add(order);
            await db.SaveChangesAsync();
            orderId = order.ORDER_ID;
            await new PaymentRepository(db).AddAsync(
                PAYMENT.Create(orderId, PaymentMethod.Card, paymentIntentId, 1000m, _clock), CancellationToken.None);
        }

        var json = JsonSerializer.Serialize(new
        {
            id = eventId, @object = "event", type = "payment_intent.succeeded",
            data = new { @object = new { id = paymentIntentId, @object = "payment_intent", amount = 100000, currency = "thb", status = "succeeded" } },
        });

        using (var scope = fixture.CreateScope())
        {
            var result = await CreateHandler(scope, chargeFee: 30m).HandleAsync(json, Sign(json), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var splitA = await db.RevenueSplits().SingleAsync(s => s.ORDER_ITEM_ID == itemAId);
            var splitB = await db.RevenueSplits().SingleAsync(s => s.ORDER_ITEM_ID == itemBId);

            // 700/1000 and 300/1000 of a 30.00 fee are both exact (21.00/9.00) — no rounding drift to
            // account for, so both paths (direct pro-rated formula and "remainder to last item") produce
            // the same numbers regardless of which item the DB happens to return first.
            Assert.Equal(21.00m, splitA.PAYMENT_FEE_AMOUNT);
            Assert.Equal(9.00m, splitB.PAYMENT_FEE_AMOUNT);
            Assert.Equal(30.00m, splitA.PAYMENT_FEE_AMOUNT + splitB.PAYMENT_FEE_AMOUNT);
        }
    }

    [Fact]
    public async Task PaidWebhook_SingleItemOrder_WithChargeFee_AllocatesFullFeeWithNoRoundingDrift()
    {
        var userId = Guid.NewGuid();
        var paymentIntentId = $"pi_{Guid.NewGuid():N}";
        var eventId = $"evt_{Guid.NewGuid():N}";
        Guid orderId, itemId;
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = await SeedCourseAsync(db);
            var order = ORDER.Create($"SU-{Guid.NewGuid():N}"[..20], userId, 1000m, 0m, 0m, 1000m, null);
            var item = order.AddItem(course.Id, course.Title, 1000m, 1000m);
            itemId = item.ORDER_ITEM_ID;
            order.MarkAwaitingPayment();
            db.Orders().Add(order);
            await db.SaveChangesAsync();
            orderId = order.ORDER_ID;
            await new PaymentRepository(db).AddAsync(
                PAYMENT.Create(orderId, PaymentMethod.Card, paymentIntentId, 1000m, _clock), CancellationToken.None);
        }

        var json = JsonSerializer.Serialize(new
        {
            id = eventId, @object = "event", type = "payment_intent.succeeded",
            data = new { @object = new { id = paymentIntentId, @object = "payment_intent", amount = 100000, currency = "thb", status = "succeeded" } },
        });

        using (var scope = fixture.CreateScope())
        {
            var result = await CreateHandler(scope, chargeFee: 25.37m).HandleAsync(json, Sign(json), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var split = await db.RevenueSplits().SingleAsync(s => s.ORDER_ITEM_ID == itemId);
            Assert.Equal(25.37m, split.PAYMENT_FEE_AMOUNT);
        }
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

    /// <summary>P11-13 (Q13.3): Hybrid course with AccessDurationDays set and one future
    /// <c>Scheduled</c> live session — the fixture the new P11-13 tests below need to prove
    /// <c>ENROLLMENT.EXPIRES_AT_UTC</c> is computed from <c>sessionStartsAtUtc</c>, not fulfillment
    /// time.</summary>
    private async Task<(COURSE Course, DateTime SessionStartsAtUtc)> SeedHybridCourseWithLiveSessionAsync(AppDbContext db, int accessDurationDays = 30)
    {
        var category = CATEGORY.Create($"category-{Guid.NewGuid():N}", "Integration", "Integration", null, null, 0);
        var instructor = INSTRUCTOR_PROFILE.Apply(Guid.NewGuid(), "Integration Instructor", null, "Integration tests");
        instructor.Approve(_clock);
        db.Categories().Add(category);
        db.InstructorProfiles().Add(instructor);
        var course = COURSE.Create($"course-{Guid.NewGuid():N}", "Hybrid fulfillment integration course", instructor.Id,
            category.Id, CourseLevel.Beginner, CourseLanguage.English, 1000m);
        course.SetThumbnail("https://example.test/course.png");
        course.SetAccessDuration(accessDurationDays);
        course.SetDeliveryFormat(DeliveryFormat.Hybrid);
        var sessionStartsAtUtc = _clock.UtcNow.AddDays(10);
        course.AddLiveSession("Live session 1", null, sessionStartsAtUtc, sessionStartsAtUtc.AddHours(1), _clock);
        course.Publish(_clock);
        db.Courses().Add(course);
        await db.SaveChangesAsync();
        return (course, sessionStartsAtUtc);
    }

    [Fact]
    public async Task PaidWebhook_HybridCourseWithScheduledSession_ComputesExpiresAtUtcFromSessionStart()
    {
        var userId = Guid.NewGuid();
        var paymentIntentId = $"pi_{Guid.NewGuid():N}";
        var eventId = $"evt_{Guid.NewGuid():N}";
        Guid orderId, courseId;
        DateTime sessionStartsAtUtc;
        const int accessDurationDays = 30;
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (course, sessionStart) = await SeedHybridCourseWithLiveSessionAsync(db, accessDurationDays);
            courseId = course.Id;
            sessionStartsAtUtc = sessionStart;
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
            var result = await CreateHandler(scope).HandleAsync(json, Sign(json), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var enrollment = await db.Enrollments().SingleAsync(e => e.USER_ID == userId && e.COURSE_ID == courseId);

            // Must equal session.StartsAtUtc.AddDays(30) exactly — not merely "in the future" or "not
            // null" — and must differ from what the pre-P11-13 formula (fulfillment-time-based) would
            // have produced, so this test cannot pass by coincidence.
            var expectedExpiresAtUtc = sessionStartsAtUtc.AddDays(accessDurationDays);
            Assert.Equal(expectedExpiresAtUtc, enrollment.EXPIRES_AT_UTC);
            Assert.True(enrollment.EXPIRES_AT_UTC > DateTime.UtcNow.AddDays(accessDurationDays - 1));
        }
    }

    [Fact]
    public async Task FreeOrder_HybridCourseWithScheduledSession_ComputesExpiresAtUtcFromSessionStart()
    {
        var userId = Guid.NewGuid();
        Guid courseId;
        DateTime sessionStartsAtUtc;
        const int accessDurationDays = 45;
        var promoCode = $"FREEHYBRID{Guid.NewGuid():N}"[..32].ToUpperInvariant();
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var (course, sessionStart) = await SeedHybridCourseWithLiveSessionAsync(db, accessDurationDays);
            courseId = course.Id;
            sessionStartsAtUtc = sessionStart;
            var promo = PROMO_CODE.Create(promoCode, PromoCodeDiscountType.Percentage, 100m, 10, 1, 0m,
                _clock.UtcNow.AddDays(-1), _clock.UtcNow.AddDays(1), PromoCodeScope.AllCourses, null);
            await new PromoCodeRepository(db).AddAsync(promo, CancellationToken.None);
        }

        Guid orderId;
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var catalog = new CatalogPriceContract(db);
            var promoRepository = new PromoCodeRepository(db);
            var learning = new LearningAccessContract(new EnrollmentRepository(db), catalog, _clock, CountUpdater(db));
            var service = new OrderService(new OrderRepository(db), promoRepository, catalog,
                new LiveScheduleReader(db), learning,
                new PricingEngine(catalog, new FlashSaleRepository(db), new BundleRepository(db), promoRepository, _clock), _clock,
                new PaymentRepository(db), new StubPaymentMethod(null));

            var result = await service.CreateAsync(userId, new CreateOrderCommand([courseId], promoCode), CancellationToken.None);
            Assert.True(result.IsSuccess);
            orderId = result.Value.Id;
        }

        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            Assert.Equal(OrderStatus.Paid, (await db.Orders().SingleAsync(o => o.ORDER_ID == orderId)).STATUS);
            var enrollment = await db.Enrollments().SingleAsync(e => e.USER_ID == userId && e.COURSE_ID == courseId);
            Assert.Equal(sessionStartsAtUtc.AddDays(accessDurationDays), enrollment.EXPIRES_AT_UTC);
        }
    }

    [Fact]
    public async Task PaidWebhook_OnDemandCourseWithAccessDuration_StillComputesExpiresAtUtcFromFulfillmentTime()
    {
        // Regression: the vast majority of courses in the system are OnDemand — this must keep behaving
        // exactly like it did before P11-13 (ExpiresAtUtc counted from the moment the webhook fulfills the
        // order, not from any live session, since an OnDemand course has none).
        var userId = Guid.NewGuid();
        var paymentIntentId = $"pi_{Guid.NewGuid():N}";
        var eventId = $"evt_{Guid.NewGuid():N}";
        Guid orderId, courseId;
        var beforeFulfillment = DateTime.UtcNow;
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var course = await SeedCourseAsync(db); // OnDemand, AccessDurationDays = 30 (see SeedCourseAsync)
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
            var result = await CreateHandler(scope).HandleAsync(json, Sign(json), CancellationToken.None);
            Assert.True(result.IsSuccess);
        }

        var afterFulfillment = DateTime.UtcNow;
        using (var scope = fixture.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            var enrollment = await db.Enrollments().SingleAsync(e => e.USER_ID == userId && e.COURSE_ID == courseId);
            Assert.NotNull(enrollment.EXPIRES_AT_UTC);
            Assert.InRange(enrollment.EXPIRES_AT_UTC!.Value, beforeFulfillment.AddDays(30), afterFulfillment.AddDays(30));
        }
    }

    private StripeWebhookHandler CreateHandler(IServiceScope scope, bool failAfterRevenueSave = false, decimal? chargeFee = null)
    {
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var catalog = new CatalogPriceContract(db);
        IRevenueSplitContract revenue = new RevenueSplitContract(new RevenueSplitRepository(db), catalog, Options.Create(new PayoutOptions()), _clock);
        if (failAfterRevenueSave) revenue = new FailingRevenueContract(revenue);
        return new StripeWebhookHandler(new StripeWebhookEventRepository(db), new PaymentRepository(db), new OrderRepository(db),
            new PromoCodeRepository(db), new PaymentOpsQueueRepository(db), catalog,
            new LiveScheduleReader(db),
            new LearningAccessContract(new EnrollmentRepository(db), catalog, _clock, CountUpdater(db)), revenue,
            scope.ServiceProvider.GetRequiredService<IEmailOutbox>(), new UserContactReader(),
            new StubPaymentMethod(chargeFee),
            Options.Create(new StripeOptions { WebhookSecret = WebhookSecret }), _clock, NullLogger<StripeWebhookHandler>.Instance);
    }

    private static string Sign(string json)
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signature = HMACSHA256.HashData(Encoding.UTF8.GetBytes(WebhookSecret), Encoding.UTF8.GetBytes($"{timestamp}.{json}"));
        return $"t={timestamp},v1={Convert.ToHexStringLower(signature)}";
    }

    /// <summary>Stub <see cref="IPaymentMethod"/> — StripeWebhookHandler only ever calls
    /// <see cref="GetChargeFeeAsync"/> on this interface, so every other member is unused here.</summary>
    private sealed class StubPaymentMethod(decimal? chargeFee) : IPaymentMethod
    {
        public Task<Result<PaymentIntentResult>> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not used by StripeWebhookHandler.");

        public Task<Result<PaymentIntentResult>> GetPaymentIntentAsync(string providerPaymentIntentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not used by StripeWebhookHandler.");

        public Task<Result> CancelPaymentIntentAsync(string providerPaymentIntentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not used by StripeWebhookHandler.");

        public Task<Result<PaymentRefundResult>> CreateRefundAsync(CreateRefundRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not used by StripeWebhookHandler.");

        public Task<Result<decimal?>> GetChargeFeeAsync(string providerPaymentIntentId, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(chargeFee));
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
