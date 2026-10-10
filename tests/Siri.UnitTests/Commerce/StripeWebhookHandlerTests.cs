using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Integrations.Payment;
using Siri.Integrations.Payment.Stripe;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Notification.Contracts;
using Siri.Modules.Payout.Contracts;
using Siri.SharedKernel;

namespace Siri.UnitTests.Commerce;

public class StripeWebhookHandlerTests
{
    private const string WebhookSecret = "whsec_test_secret_key_12345";
    private readonly FakeStripeWebhookEventRepository _webhookEventRepo = new();
    private readonly FakePaymentRepository _paymentRepo = new();
    private readonly FakeOrderRepository _orderRepo = new();
    private readonly FakePromoCodeRepository _promoCodeRepo = new();
    private readonly FakePaymentOpsQueueRepository _opsQueueRepo = new();
    private readonly FakeCatalogPriceContract _catalogPriceContract = new();
    private readonly FakeLiveScheduleReader _liveScheduleReader = new();
    private readonly FakeLearningAccessContract _learningAccessContract = new();
    private readonly FakeRevenueSplitContract _revenueSplitContract = new();
    private readonly FakeEmailOutbox _emailOutbox = new();
    private readonly FakeUserContactReader _userContactReader = new();
    private readonly FakePaymentMethod _paymentMethod = new();
    private readonly FakeClock _clock = new(new DateTime(2026, 8, 21, 10, 0, 0, DateTimeKind.Utc));

    private StripeWebhookHandler CreateHandler(string secret = WebhookSecret)
    {
        var options = Options.Create(new StripeOptions
        {
            SecretKey = "sk_test_123",
            PublishableKey = "pk_test_123",
            WebhookSecret = secret,
        });

        return new StripeWebhookHandler(
            _webhookEventRepo,
            _paymentRepo,
            _orderRepo,
            _promoCodeRepo,
            _opsQueueRepo,
            _catalogPriceContract,
            _liveScheduleReader,
            _learningAccessContract,
            _revenueSplitContract,
            _emailOutbox,
            _userContactReader,
            _paymentMethod,
            options,
            _clock,
            NullLogger<StripeWebhookHandler>.Instance);
    }

    private static string GenerateStripeSignature(string payload, string secret, long? timestamp = null)
    {
        var ts = timestamp ?? DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var signedPayload = $"{ts}.{payload}";
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(secret));
        var hash = Convert.ToHexStringLower(hmac.ComputeHash(Encoding.UTF8.GetBytes(signedPayload)));
        return $"t={ts},v1={hash}";
    }

    private static string BuildStripeEventJson(string eventId, string eventType, string paymentIntentId, long amount = 100000)
    {
        var eventObj = new
        {
            id = eventId,
            @object = "event",
            type = eventType,
            data = new
            {
                @object = new
                {
                    id = paymentIntentId,
                    @object = "payment_intent",
                    amount = amount,
                    currency = "thb",
                    status = eventType == "payment_intent.succeeded" ? "succeeded" : "failed",
                }
            }
        };

        return JsonSerializer.Serialize(eventObj);
    }

    [Fact]
    public async Task HandleAsync_MissingSignatureHeader_ReturnsValidationError()
    {
        var handler = CreateHandler();
        var result = await handler.HandleAsync("{}", null, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("CHANGE_ME_DEV_ONLY_whsec_placeholder_key")]
    public async Task HandleAsync_WebhookSecretMissingOrPlaceholder_RejectsEvenWithSignatureMadeFromIt(string secret)
    {
        // A placeholder secret is publicly known (it was committed in appsettings), so a "valid"
        // signature computed from it proves nothing — the event must be refused with 503-mapped
        // payment.provider_not_configured and never processed.
        var handler = CreateHandler(secret);
        var payload = BuildStripeEventJson("evt_placeholder_1", "payment_intent.succeeded", "pi_1");
        var forgedSignature = GenerateStripeSignature(payload, string.IsNullOrWhiteSpace(secret) ? "x" : secret);

        var result = await handler.HandleAsync(payload, forgedSignature, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal(PaymentProviderErrors.ProviderNotConfiguredCode, result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_InvalidSignature_ReturnsValidationError()
    {
        var handler = CreateHandler();
        var payload = BuildStripeEventJson("evt_1", "payment_intent.succeeded", "pi_1");
        var badSignature = "t=1700000000,v1=invalid_hash_value";

        var result = await handler.HandleAsync(payload, badSignature, CancellationToken.None);

        Assert.True(result.IsFailure);
        Assert.Equal("validation", result.Error.Code);
    }

    [Fact]
    public async Task HandleAsync_ValidSignature_ProcessesEventAndGuaranteesIdempotency()
    {
        var handler = CreateHandler();
        var eventId = "evt_idempotency_1";
        var piId = "pi_idem_1";

        var order = ORDER.Create("ORD-001", Guid.NewGuid(), 1000m, 0m, 0m, 1000m);
        order.MarkAwaitingPayment();
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, piId, 1000m, _clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var payload = BuildStripeEventJson(eventId, "payment_intent.succeeded", piId);
        var signature = GenerateStripeSignature(payload, WebhookSecret);

        // First delivery: processes successfully
        var result1 = await handler.HandleAsync(payload, signature, CancellationToken.None);
        Assert.True(result1.IsSuccess);
        Assert.Equal("Event processed", result1.Value);
        Assert.Equal(OrderStatus.Paid, order.STATUS);
        Assert.Equal(PaymentStatus.Succeeded, payment.STATUS);

        // Second delivery (same event id): idempotent no-op!
        var result2 = await handler.HandleAsync(payload, signature, CancellationToken.None);
        Assert.True(result2.IsSuccess);
        Assert.Equal("Event already processed", result2.Value);
    }

    [Fact]
    public async Task HandleAsync_PaymentSucceeded_GrantsEnrollmentPerCourseAndQueuesReceiptToRealEmail()
    {
        var handler = CreateHandler();
        var eventId = "evt_enroll_1";
        var piId = "pi_enroll_1";
        var buyerId = Guid.NewGuid();
        var course1 = Guid.NewGuid();
        var course2 = Guid.NewGuid();

        // AccessDurationDays = 30 for course1 (time-limited), null for course2 (lifetime) — proves
        // EnrollUserAsync receives a real, course-specific expiry instead of always-null/always-lifetime.
        _catalogPriceContract.Prices[course1] = new CoursePriceInfo(course1, "COURSE 1", 1000m, Guid.NewGuid(), 30);
        _catalogPriceContract.Prices[course2] = new CoursePriceInfo(course2, "COURSE 2", 500m, Guid.NewGuid(), null);
        _userContactReader.Emails[buyerId] = "buyer@example.test";

        var order = ORDER.Create("ORD-ENROLL", buyerId, 1500m, 0m, 0m, 1500m);
        order.AddItem(course1, "COURSE 1", 1000m, 1000m);
        order.AddItem(course2, "COURSE 2", 500m, 500m);
        order.MarkAwaitingPayment();
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, piId, 1500m, _clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var payload = BuildStripeEventJson(eventId, "payment_intent.succeeded", piId, 150000);
        var signature = GenerateStripeSignature(payload, WebhookSecret);

        var result = await handler.HandleAsync(payload, signature, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(2, _learningAccessContract.Grants.Count);
        Assert.Contains(_learningAccessContract.Grants, g => g.CourseId == course1 && g.OrderId == order.ORDER_ID && g.ExpiresAtUtc == _clock.UtcNow.AddDays(30));
        Assert.Contains(_learningAccessContract.Grants, g => g.CourseId == course2 && g.OrderId == order.ORDER_ID && g.ExpiresAtUtc == null);

        // Proves the batch: one EnrollUserInCoursesAsync call carrying both courses, not two
        // EnrollUserAsync calls (one per course, the old N+1 shape this fix removes).
        Assert.Equal(1, _learningAccessContract.EnrollUserInCoursesCallCount);
        Assert.Equal(0, _learningAccessContract.EnrollUserCallCount);

        Assert.Single(_revenueSplitContract.Recorded);

        // The real user email, not a fabricated @example.test fallback derived from the user id.
        Assert.Single(_emailOutbox.Sent);
        Assert.Equal("buyer@example.test", _emailOutbox.Sent[0].ToEmail);
    }

    // P11-13 (Q13.3): Live/Hybrid courses count AccessDurationDays from the first scheduled live session's
    // StartsAtUtc, not the payment-confirmation time — otherwise a student who buys weeks before a
    // scheduled cohort starts loses access time waiting for it.

    [Fact]
    public async Task HandleAsync_PaymentSucceeded_CourseHasEarliestScheduledSession_ComputesExpiresAtUtcFromSessionStart()
    {
        var handler = CreateHandler();
        var eventId = "evt_p11_13_session";
        var piId = "pi_p11_13_session";
        var buyerId = Guid.NewGuid();
        var courseId = Guid.NewGuid();

        _catalogPriceContract.Prices[courseId] = new CoursePriceInfo(courseId, "COURSE 1", 1000m, Guid.NewGuid(), 30);

        var sessionStartsAtUtc = _clock.UtcNow.AddDays(10);
        _liveScheduleReader.EarliestSessionByCourseId[courseId] =
            new LiveSessionInfo(Guid.NewGuid(), courseId, "Session 1", sessionStartsAtUtc, sessionStartsAtUtc.AddHours(1), LiveSessionStatus.Scheduled, null);

        var order = ORDER.Create("ORD-P11-13-A", buyerId, 1000m, 0m, 0m, 1000m);
        order.AddItem(courseId, "COURSE 1", 1000m, 1000m);
        order.MarkAwaitingPayment();
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, piId, 1000m, _clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var payload = BuildStripeEventJson(eventId, "payment_intent.succeeded", piId);
        var signature = GenerateStripeSignature(payload, WebhookSecret);

        var result = await handler.HandleAsync(payload, signature, CancellationToken.None);

        Assert.True(result.IsSuccess);

        // Must equal session.StartsAtUtc.AddDays(30), and must NOT equal clock.UtcNow.AddDays(30) — the
        // two are asserted separately so this test cannot pass by coincidence.
        var expectedExpiresAtUtc = sessionStartsAtUtc.AddDays(30);
        var fallbackExpiresAtUtc = _clock.UtcNow.AddDays(30);
        Assert.NotEqual(expectedExpiresAtUtc, fallbackExpiresAtUtc);
        Assert.Contains(_learningAccessContract.Grants, g => g.CourseId == courseId && g.ExpiresAtUtc == expectedExpiresAtUtc);
    }

    [Fact]
    public async Task HandleAsync_PaymentSucceeded_CourseHasNoScheduledSession_FallsBackToNowPlusAccessDurationDays()
    {
        var handler = CreateHandler();
        var eventId = "evt_p11_13_nosession";
        var piId = "pi_p11_13_nosession";
        var buyerId = Guid.NewGuid();
        var courseId = Guid.NewGuid();

        // No entry in _liveScheduleReader.EarliestSessionByCourseId — covers both "the course is
        // OnDemand" and "the course is Live/Hybrid but nothing is scheduled yet"; production code cannot
        // and must not try to tell those two apart (see class comment above), so one test stands in for
        // both.
        _catalogPriceContract.Prices[courseId] = new CoursePriceInfo(courseId, "COURSE 1", 1000m, Guid.NewGuid(), 30);

        var order = ORDER.Create("ORD-P11-13-B", buyerId, 1000m, 0m, 0m, 1000m);
        order.AddItem(courseId, "COURSE 1", 1000m, 1000m);
        order.MarkAwaitingPayment();
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, piId, 1000m, _clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var payload = BuildStripeEventJson(eventId, "payment_intent.succeeded", piId);
        var signature = GenerateStripeSignature(payload, WebhookSecret);

        var result = await handler.HandleAsync(payload, signature, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(_learningAccessContract.Grants, g => g.CourseId == courseId && g.ExpiresAtUtc == _clock.UtcNow.AddDays(30));
    }

    [Fact]
    public async Task HandleAsync_PaymentSucceeded_NullAccessDurationDays_GrantsLifetimeAccessWithoutQueryingLiveSchedule()
    {
        var handler = CreateHandler();
        var eventId = "evt_p11_13_lifetime";
        var piId = "pi_p11_13_lifetime";
        var buyerId = Guid.NewGuid();
        var courseId = Guid.NewGuid();

        // AccessDurationDays = null (lifetime access) — the short-circuit `AccessDurationDays is { } days`
        // guard must skip GetEarliestScheduledSessionAsync entirely. Every other member of
        // FakeLiveScheduleReader throws, so this test would fail loudly if production code ever called
        // GetSessionsForCourseAsync/GetUpcomingSessionsAsync/GetSessionAsync here. A session is seeded
        // anyway (GetEarliestScheduledSessionAsync itself would not throw if called) to prove it is
        // genuinely never consulted — the resulting ExpiresAtUtc stays null either way.
        _catalogPriceContract.Prices[courseId] = new CoursePriceInfo(courseId, "COURSE 1", 1000m, Guid.NewGuid(), null);
        _liveScheduleReader.EarliestSessionByCourseId[courseId] =
            new LiveSessionInfo(Guid.NewGuid(), courseId, "Session 1", _clock.UtcNow.AddDays(10), _clock.UtcNow.AddDays(10).AddHours(1), LiveSessionStatus.Scheduled, null);

        var order = ORDER.Create("ORD-P11-13-C", buyerId, 1000m, 0m, 0m, 1000m);
        order.AddItem(courseId, "COURSE 1", 1000m, 1000m);
        order.MarkAwaitingPayment();
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, piId, 1000m, _clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var payload = BuildStripeEventJson(eventId, "payment_intent.succeeded", piId);
        var signature = GenerateStripeSignature(payload, WebhookSecret);

        var result = await handler.HandleAsync(payload, signature, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Contains(_learningAccessContract.Grants, g => g.CourseId == courseId && g.ExpiresAtUtc == null);
    }

    /// <summary>P11-09 §1.4: charge.balance_transaction.fee is per-PaymentIntent, not per-item, so a
    /// multi-course order must pro-rate it across items by each item's share of the order total —
    /// remainder to the last item. 700/1000 and 300/1000 of a 30.00 fee are both exact (21.00/9.00),
    /// so this also proves the sum equals totalFee exactly, not just approximately.</summary>
    [Fact]
    public async Task HandleAsync_PaymentSucceeded_TwoItemsWithChargeFee_AllocatesFeeProportionallyExactly()
    {
        _paymentMethod.ChargeFeeToReturn = 30.00m;
        var handler = CreateHandler();
        var eventId = "evt_fee_prorate_1";
        var piId = "pi_fee_prorate_1";
        var buyerId = Guid.NewGuid();
        var course1 = Guid.NewGuid();
        var course2 = Guid.NewGuid();
        var instructor1 = Guid.NewGuid();
        var instructor2 = Guid.NewGuid();

        _catalogPriceContract.Prices[course1] = new CoursePriceInfo(course1, "COURSE 1", 700m, instructor1, null);
        _catalogPriceContract.Prices[course2] = new CoursePriceInfo(course2, "COURSE 2", 300m, instructor2, null);

        var order = ORDER.Create("ORD-FEE-2", buyerId, 1000m, 0m, 0m, 1000m);
        var item1 = order.AddItem(course1, "COURSE 1", 700m, 700m);
        var item2 = order.AddItem(course2, "COURSE 2", 300m, 300m);
        order.MarkAwaitingPayment();
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.Card, piId, 1000m, _clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var payload = BuildStripeEventJson(eventId, "payment_intent.succeeded", piId, 100000);
        var signature = GenerateStripeSignature(payload, WebhookSecret);

        var result = await handler.HandleAsync(payload, signature, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var (_, items) = Assert.Single(_revenueSplitContract.Recorded);
        var split1 = items.Single(i => i.OrderItemId == item1.ORDER_ITEM_ID);
        var split2 = items.Single(i => i.OrderItemId == item2.ORDER_ITEM_ID);
        Assert.Equal(21.00m, split1.PaymentFee);
        Assert.Equal(9.00m, split2.PaymentFee);
        Assert.Equal(30.00m, split1.PaymentFee + split2.PaymentFee);
    }

    [Fact]
    public async Task HandleAsync_PaymentSucceeded_SingleItemWithChargeFee_AllocatesFullFeeNoRoundingDrift()
    {
        _paymentMethod.ChargeFeeToReturn = 25.37m;
        var handler = CreateHandler();
        var eventId = "evt_fee_single_1";
        var piId = "pi_fee_single_1";
        var buyerId = Guid.NewGuid();
        var course = Guid.NewGuid();
        var instructor = Guid.NewGuid();

        _catalogPriceContract.Prices[course] = new CoursePriceInfo(course, "COURSE", 1000m, instructor, null);

        var order = ORDER.Create("ORD-FEE-1", buyerId, 1000m, 0m, 0m, 1000m);
        var item = order.AddItem(course, "COURSE", 1000m, 1000m);
        order.MarkAwaitingPayment();
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.Card, piId, 1000m, _clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var payload = BuildStripeEventJson(eventId, "payment_intent.succeeded", piId, 100000);
        var signature = GenerateStripeSignature(payload, WebhookSecret);

        var result = await handler.HandleAsync(payload, signature, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var (_, items) = Assert.Single(_revenueSplitContract.Recorded);
        var split = Assert.Single(items);
        Assert.Equal(item.ORDER_ITEM_ID, split.OrderItemId);
        Assert.Equal(25.37m, split.PaymentFee);
    }

    /// <summary>Backward-compatible fallback path (PromptPay before this task, and Card whenever the fee
    /// isn't settled yet): PaymentFee stays null so RevenueSplitContract computes its own
    /// EstimatedPaymentFeePercent-based estimate — unchanged from pre-P11-09 behavior.</summary>
    [Fact]
    public async Task HandleAsync_PaymentSucceeded_NoChargeFeeAvailable_LeavesPaymentFeeNullForContractFallback()
    {
        _paymentMethod.ChargeFeeToReturn = null;
        var handler = CreateHandler();
        var eventId = "evt_fee_null_1";
        var piId = "pi_fee_null_1";
        var buyerId = Guid.NewGuid();
        var course = Guid.NewGuid();

        _catalogPriceContract.Prices[course] = new CoursePriceInfo(course, "COURSE", 1000m, Guid.NewGuid(), null);

        var order = ORDER.Create("ORD-FEE-NULL", buyerId, 1000m, 0m, 0m, 1000m);
        order.AddItem(course, "COURSE", 1000m, 1000m);
        order.MarkAwaitingPayment();
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, piId, 1000m, _clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var payload = BuildStripeEventJson(eventId, "payment_intent.succeeded", piId, 100000);
        var signature = GenerateStripeSignature(payload, WebhookSecret);

        var result = await handler.HandleAsync(payload, signature, CancellationToken.None);

        Assert.True(result.IsSuccess);
        var (_, items) = Assert.Single(_revenueSplitContract.Recorded);
        var split = Assert.Single(items);
        Assert.Null(split.PaymentFee);
    }

    /// <summary>Owner decision 2026-10-10: while an admin amount override lowered the charge, revenue split runs on
    /// the money actually collected — instructors must never be credited the list price Stripe never received.
    /// Real Stripe fee is a single value for the charge, pro-rated by line share (unchanged ratio logic).</summary>
    [Fact]
    public async Task HandleAsync_PaymentSucceeded_PaidThroughAmountOverride_SplitsOnThePaidAmountNotListPrice()
    {
        _paymentMethod.ChargeFeeToReturn = 1.00m;
        var handler = CreateHandler();
        var buyerId = Guid.NewGuid();
        var course1 = Guid.NewGuid();
        var course2 = Guid.NewGuid();

        _catalogPriceContract.Prices[course1] = new CoursePriceInfo(course1, "COURSE 1", 700m, Guid.NewGuid(), null);
        _catalogPriceContract.Prices[course2] = new CoursePriceInfo(course2, "COURSE 2", 300m, Guid.NewGuid(), null);
        _userContactReader.Emails[buyerId] = "buyer@example.test";

        var order = ORDER.Create("ORD-OVR-SPLIT", buyerId, 1000m, 0m, 0m, 1000m);
        var item1 = order.AddItem(course1, "COURSE 1", 700m, 700m);
        var item2 = order.AddItem(course2, "COURSE 2", 300m, 300m);
        order.MarkAwaitingPayment();
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, "pi_ovr_split", 20m, _clock, originalAmount: 1000m);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var payload = BuildStripeEventJson("evt_ovr_split", "payment_intent.succeeded", "pi_ovr_split", 2000);
        var result = await handler.HandleAsync(payload, GenerateStripeSignature(payload, WebhookSecret), CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(OrderStatus.Paid, order.STATUS);
        var (_, items) = Assert.Single(_revenueSplitContract.Recorded);
        var split1 = items.Single(i => i.OrderItemId == item1.ORDER_ITEM_ID);
        var split2 = items.Single(i => i.OrderItemId == item2.ORDER_ITEM_ID);
        Assert.Equal(14.00m, split1.GrossAmount);
        Assert.Equal(6.00m, split2.GrossAmount);
        Assert.Equal(20m, split1.GrossAmount + split2.GrossAmount);   // exactly what was collected
        Assert.Equal(0.70m, split1.PaymentFee);                      // 1.00 fee × 700/1000
        Assert.Equal(0.30m, split2.PaymentFee);

        // Enrollment is granted exactly as for a normal purchase.
        Assert.Equal(2, _learningAccessContract.Grants.Count);
    }

    [Fact]
    public async Task HandleAsync_PaymentSucceeded_PaidThroughAmountOverride_ReceiptShowsPaidAmountNotListPrice()
    {
        var handler = CreateHandler();
        var buyerId = Guid.NewGuid();
        var course = Guid.NewGuid();
        _catalogPriceContract.Prices[course] = new CoursePriceInfo(course, "COURSE", 1890m, Guid.NewGuid(), null);
        _userContactReader.Emails[buyerId] = "buyer@example.test";

        var order = ORDER.Create("ORD-OVR-MAIL", buyerId, 1890m, 0m, 0m, 1890m);
        order.AddItem(course, "COURSE", 1890m, 1890m);
        order.MarkAwaitingPayment();
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, "pi_ovr_mail", 20m, _clock, originalAmount: 1890m);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var payload = BuildStripeEventJson("evt_ovr_mail", "payment_intent.succeeded", "pi_ovr_mail", 2000);
        var result = await handler.HandleAsync(payload, GenerateStripeSignature(payload, WebhookSecret), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var mail = Assert.Single(_emailOutbox.Sent);
        Assert.Contains("฿20.00", mail.BodyHtml);
        Assert.DoesNotContain("1,890", mail.BodyHtml);
    }

    [Fact]
    public async Task HandleAsync_PaymentSucceeded_NormalPayment_GrossIsStillTheFullLineTotal()
    {
        _paymentMethod.ChargeFeeToReturn = null;
        var handler = CreateHandler();
        var buyerId = Guid.NewGuid();
        var course = Guid.NewGuid();
        _catalogPriceContract.Prices[course] = new CoursePriceInfo(course, "COURSE", 1000m, Guid.NewGuid(), null);

        var order = ORDER.Create("ORD-NORMAL-GROSS", buyerId, 1000m, 0m, 0m, 1000m);
        order.AddItem(course, "COURSE", 1000m, 1000m);
        order.MarkAwaitingPayment();
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, "pi_normal_gross", 1000m, _clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var payload = BuildStripeEventJson("evt_normal_gross", "payment_intent.succeeded", "pi_normal_gross", 100000);
        var result = await handler.HandleAsync(payload, GenerateStripeSignature(payload, WebhookSecret), CancellationToken.None);

        Assert.True(result.IsSuccess);
        var (_, items) = Assert.Single(_revenueSplitContract.Recorded);
        Assert.Equal(1000m, Assert.Single(items).GrossAmount);
    }

    [Fact]
    public async Task HandleAsync_PaymentSucceeded_ForExpiredOrder_FlagsToOpsQueue()
    {
        var handler = CreateHandler();
        var eventId = "evt_expired_order_1";
        var piId = "pi_expired_1";

        var order = ORDER.Create("ORD-EXPIRED", Guid.NewGuid(), 2000m, 0m, 0m, 2000m);
        // Order cancelled/expired before payment webhook arrived
        order.MarkAwaitingPayment();
        var statusProp = typeof(ORDER).GetProperty(nameof(ORDER.STATUS));
        statusProp!.SetValue(order, OrderStatus.Cancelled);
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, piId, 2000m, _clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var payload = BuildStripeEventJson(eventId, "payment_intent.succeeded", piId);
        var signature = GenerateStripeSignature(payload, WebhookSecret);

        var result = await handler.HandleAsync(payload, signature, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Succeeded, payment.STATUS);

        // Ensure money was not swallowed: flagged to payment ops queue!
        var queuedEntries = await _opsQueueRepo.GetOpenEntriesAsync(CancellationToken.None);
        var queueEntry = queuedEntries.FirstOrDefault(q => q.PAYMENT_ID == payment.PAYMENT_ID);
        Assert.NotNull(queueEntry);
        Assert.Contains("expired", queueEntry.REASON, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_PaymentFailed_MarksPaymentFailed()
    {
        var handler = CreateHandler();
        var eventId = "evt_failed_1";
        var piId = "pi_failed_1";

        var order = ORDER.Create("ORD-FAIL", Guid.NewGuid(), 500m, 0m, 0m, 500m);
        order.MarkAwaitingPayment();
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, piId, 500m, _clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var payload = BuildStripeEventJson(eventId, "payment_intent.payment_failed", piId);
        var signature = GenerateStripeSignature(payload, WebhookSecret);

        var result = await handler.HandleAsync(payload, signature, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Failed, payment.STATUS);
    }

    [Fact]
    public async Task HandleAsync_PaymentCanceled_MarksPaymentExpired()
    {
        var handler = CreateHandler();
        var eventId = "evt_canceled_1";
        var piId = "pi_canceled_1";

        var order = ORDER.Create("ORD-CANCEL", Guid.NewGuid(), 500m, 0m, 0m, 500m);
        order.MarkAwaitingPayment();
        await _orderRepo.AddAsync(order, CancellationToken.None);

        var payment = PAYMENT.Create(order.ORDER_ID, PaymentMethod.PromptPay, piId, 500m, _clock);
        await _paymentRepo.AddAsync(payment, CancellationToken.None);

        var payload = BuildStripeEventJson(eventId, "payment_intent.canceled", piId);
        var signature = GenerateStripeSignature(payload, WebhookSecret);

        var result = await handler.HandleAsync(payload, signature, CancellationToken.None);

        Assert.True(result.IsSuccess);
        Assert.Equal(PaymentStatus.Expired, payment.STATUS);
    }

    private sealed class FakeStripeWebhookEventRepository : IStripeWebhookEventRepository
    {
        private readonly List<STRIPE_WEBHOOK_EVENT> _events = [];

        public Task<STRIPE_WEBHOOK_EVENT?> GetByStripeEventIdAsync(string stripeEventId, CancellationToken cancellationToken) =>
            Task.FromResult(_events.FirstOrDefault(e => e.STRIPE_EVENT_ID == stripeEventId));

        public Task AddAsync(STRIPE_WEBHOOK_EVENT webhookEvent, CancellationToken cancellationToken)
        {
            _events.Add(webhookEvent);
            return Task.CompletedTask;
        }
    }

    private sealed class FakePaymentRepository : IPaymentRepository
    {
        private readonly List<PAYMENT> _payments = [];

        public Task<PAYMENT?> GetByIdAsync(Guid paymentId, CancellationToken cancellationToken) =>
            Task.FromResult(_payments.FirstOrDefault(p => p.PAYMENT_ID == paymentId));

        public Task<PAYMENT?> GetByProviderPaymentIntentIdAsync(string providerPaymentIntentId, CancellationToken cancellationToken) =>
            Task.FromResult(_payments.FirstOrDefault(p => p.PROVIDER_PAYMENT_INTENT_ID == providerPaymentIntentId));

        public Task AddAsync(PAYMENT payment, CancellationToken cancellationToken)
        {
            _payments.Add(payment);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PAYMENT>> GetPendingByOrderIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PAYMENT>>(_payments.Where(p => p.ORDER_ID == orderId && (p.STATUS == PaymentStatus.Pending || p.STATUS == PaymentStatus.Processing)).ToList());
    }

    private sealed class FakeOrderRepository : IOrderRepository
    {
        private readonly List<ORDER> _orders = [];

        public Task<ORDER?> GetByIdAsync(Guid orderId, CancellationToken cancellationToken) =>
            Task.FromResult(_orders.FirstOrDefault(o => o.ORDER_ID == orderId));

        public Task AddAsync(ORDER order, CancellationToken cancellationToken)
        {
            _orders.Add(order);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<ORDER>> GetActiveByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ORDER>>(_orders.Where(o => o.USER_ID == userId).ToList());

        public Task<(IReadOnlyList<ORDER> Items, int TotalCount)> ListByUserIdAsync(Guid userId, int page, int pageSize, CancellationToken cancellationToken)
        {
            var userOrders = _orders.Where(o => o.USER_ID == userId).ToList();
            var items = userOrders.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult<(IReadOnlyList<ORDER> Items, int TotalCount)>((items, userOrders.Count));
        }

        public Task<IReadOnlyList<ORDER>> GetStaleAwaitingPaymentOrdersAsync(DateTime cutoffUtc, int batchSize, CancellationToken cancellationToken)
        {
            var result = _orders
                .Where(o => o.STATUS == OrderStatus.AwaitingPayment && o.CreatedAtUtc <= cutoffUtc)
                .OrderBy(o => o.CreatedAtUtc)
                .Take(batchSize)
                .ToList();
            return Task.FromResult<IReadOnlyList<ORDER>>(result);
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;

        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken cancellationToken) => operation();

        public Task ExecuteInTransactionAsync(Func<Task> operation, CancellationToken cancellationToken) => operation();
    }

    private sealed class FakePromoCodeRepository : IPromoCodeRepository
    {
        public readonly Dictionary<Guid, PROMO_CODE> Codes = [];
        public readonly List<PROMO_REDEMPTION> Redemptions = [];

        public Task<PROMO_CODE?> GetByIdAsync(Guid promoCodeId, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.TryGetValue(promoCodeId, out var code) ? code : null);

        public Task<PROMO_CODE?> GetByCodeAsync(string code, CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Values.FirstOrDefault(p => p.CODE.Equals(code, StringComparison.OrdinalIgnoreCase)));

        public Task AddAsync(PROMO_CODE promoCode, CancellationToken cancellationToken)
        {
            Codes[promoCode.PROMO_CODE_ID] = promoCode;
            return Task.CompletedTask;
        }

        public Task<bool> TryRedeemAsync(Guid promoCodeId, Guid orderId, Guid userId, int maxPerUser, IClock clock, CancellationToken cancellationToken)
        {
            if (!Codes.TryGetValue(promoCodeId, out var code)) return Task.FromResult(false);
            if (!code.IS_ACTIVE || code.REDEEMED_COUNT >= code.MAX_REDEMPTIONS) return Task.FromResult(false);
            var userCount = Redemptions.Count(r => r.PROMO_CODE_ID == promoCodeId && r.USER_ID == userId);
            if (userCount >= maxPerUser) return Task.FromResult(false);

            Redemptions.Add(PROMO_REDEMPTION.Create(promoCodeId, orderId, userId, clock));
            return Task.FromResult(true);
        }

        public Task<int> GetUserRedemptionCountAsync(Guid promoCodeId, Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Redemptions.Count(r => r.PROMO_CODE_ID == promoCodeId && r.USER_ID == userId));

        public Task RevertRedemptionAsync(Guid promoCodeId, Guid orderId, CancellationToken cancellationToken)
        {
            Redemptions.RemoveAll(r => r.PROMO_CODE_ID == promoCodeId && r.ORDER_ID == orderId);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PROMO_CODE>> ListAsync(int page, int pageSize, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PROMO_CODE>>(Codes.Values.Skip((page - 1) * pageSize).Take(pageSize).ToList());

        public Task<int> CountAsync(CancellationToken cancellationToken) =>
            Task.FromResult(Codes.Count);
    }

    private sealed class FakePaymentOpsQueueRepository : IPaymentOpsQueueRepository
    {
        private readonly List<PAYMENT_OPS_QUEUE> _entries = [];

        public Task<PAYMENT_OPS_QUEUE?> GetByIdAsync(Guid paymentOpsQueueId, CancellationToken cancellationToken) =>
            Task.FromResult(_entries.FirstOrDefault(e => e.PAYMENT_OPS_QUEUE_ID == paymentOpsQueueId));

        public Task AddAsync(PAYMENT_OPS_QUEUE entry, CancellationToken cancellationToken)
        {
            _entries.Add(entry);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<PAYMENT_OPS_QUEUE>> GetOpenEntriesAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<PAYMENT_OPS_QUEUE>>(_entries.Where(e => e.STATUS == PaymentOpsQueueStatus.Open).ToList());

        public Task<(IReadOnlyList<PAYMENT_OPS_QUEUE> Items, int TotalCount)> ListAsync(PaymentOpsQueueStatus? status, int page, int pageSize, CancellationToken cancellationToken)
        {
            var query = _entries.AsEnumerable();
            if (status.HasValue) query = query.Where(e => e.STATUS == status.Value);
            var list = query.ToList();
            var items = list.Skip((page - 1) * pageSize).Take(pageSize).ToList();
            return Task.FromResult<(IReadOnlyList<PAYMENT_OPS_QUEUE> Items, int TotalCount)>((items, list.Count));
        }

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeClock(DateTime utcNow) : IClock
    {
        public DateTime UtcNow { get; } = utcNow;
    }

    private sealed class FakeCatalogPriceContract : ICatalogPriceContract
    {
        public readonly Dictionary<Guid, CoursePriceInfo> Prices = [];

        public Task<IReadOnlyDictionary<Guid, CoursePriceInfo>> GetPublishedCoursePricesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken)
        {
            var dict = courseIds.Where(Prices.ContainsKey).ToDictionary(id => id, id => Prices[id]);
            return Task.FromResult<IReadOnlyDictionary<Guid, CoursePriceInfo>>(dict);
        }

        public Task<bool> IsEpisodeFreePreviewAsync(Guid episodeId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<Guid?> GetCourseIdForEpisodeAsync(Guid episodeId, CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);

        public Task<bool> IsInstructorOwnerOfEpisodeAsync(Guid episodeId, Guid instructorUserId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> IsInstructorOwnerOfCourseAsync(Guid courseId, Guid instructorUserId, CancellationToken cancellationToken) => Task.FromResult(false);
        public Task<int> GetPendingReviewsCountAsync(CancellationToken cancellationToken) => Task.FromResult(0);
        public Task<IReadOnlyDictionary<Guid, string>> GetCourseTitlesAsync(IEnumerable<Guid> courseIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, string>>(new Dictionary<Guid, string>());
        public Task<IReadOnlyDictionary<Guid, decimal>> GetInstructorRevenueSharePercentsAsync(IEnumerable<Guid> instructorIds, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyDictionary<Guid, decimal>>(new Dictionary<Guid, decimal>());
    }

    private sealed class FakeLearningAccessContract : ILearningAccessContract
    {
        public readonly List<(Guid UserId, Guid CourseId, Guid? OrderId, string Source, DateTime? ExpiresAtUtc)> Grants = [];

        // Proves the webhook handler calls the batched overload once for the whole order instead of
        // looping EnrollUserAsync once per course (the N+1 this fix removes).
        public int EnrollUserCallCount;
        public int EnrollUserInCoursesCallCount;

        public Task<bool> CanUserAccessEpisodeAsync(Guid userId, Guid episodeId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<bool> HasActiveEnrollmentAsync(Guid userId, Guid courseId, CancellationToken cancellationToken) => Task.FromResult(false);

        public Task<Result> EnrollUserAsync(Guid userId, Guid courseId, Guid? orderId, string source, DateTime? expiresAtUtc, CancellationToken cancellationToken)
        {
            EnrollUserCallCount++;
            Grants.Add((userId, courseId, orderId, source, expiresAtUtc));
            return Task.FromResult(Result.Success());
        }

        public Task<Result> EnrollUserInCoursesAsync(Guid userId, string source, IReadOnlyCollection<CourseEnrollmentGrant> grants, CancellationToken cancellationToken)
        {
            EnrollUserInCoursesCallCount++;
            foreach (var grant in grants)
            {
                Grants.Add((userId, grant.CourseId, grant.OrderId, source, grant.ExpiresAtUtc));
            }

            return Task.FromResult(Result.Success());
        }
    }

    /// <summary>P11-13 (Q13.3): only <see cref="GetEarliestScheduledSessionAsync"/> is used by
    /// <see cref="StripeWebhookHandler"/> — every other member throws so a test would fail loudly if the
    /// production code path ever changed to call one of them.</summary>
    private sealed class FakeLiveScheduleReader : ILiveScheduleReader
    {
        public readonly Dictionary<Guid, LiveSessionInfo> EarliestSessionByCourseId = [];

        public Task<IReadOnlyList<LiveSessionInfo>> GetSessionsForCourseAsync(Guid courseId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not used by this handler under test — only GetEarliestScheduledSessionAsync is called.");

        public Task<IReadOnlyList<LiveSessionInfo>> GetUpcomingSessionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not used by this handler under test — only GetEarliestScheduledSessionAsync is called.");

        public Task<LiveSessionInfo?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not used by this handler under test — only GetEarliestScheduledSessionAsync is called.");

        public Task<LiveSessionInfo?> GetEarliestScheduledSessionAsync(Guid courseId, CancellationToken cancellationToken) =>
            Task.FromResult(EarliestSessionByCourseId.TryGetValue(courseId, out var session) ? session : null);
    }

    /// <summary>Only <see cref="GetChargeFeeAsync"/> is exercised by <c>StripeWebhookHandler</c> —
    /// every other member throws since this fake is never used for the create/get/cancel/refund flows.</summary>
    private sealed class FakePaymentMethod : IPaymentMethod
    {
        public decimal? ChargeFeeToReturn { get; set; }

        public Task<Result<PaymentIntentResult>> CreatePaymentIntentAsync(CreatePaymentIntentRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not used by StripeWebhookHandler.");

        public Task<Result<PaymentIntentResult>> GetPaymentIntentAsync(string providerPaymentIntentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not used by StripeWebhookHandler.");

        public Task<Result> CancelPaymentIntentAsync(string providerPaymentIntentId, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not used by StripeWebhookHandler.");

        public Task<Result<PaymentRefundResult>> CreateRefundAsync(CreateRefundRequest request, CancellationToken cancellationToken) =>
            throw new NotSupportedException("Not used by StripeWebhookHandler.");

        public Task<Result<decimal?>> GetChargeFeeAsync(string providerPaymentIntentId, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success(ChargeFeeToReturn));
    }

    private sealed class FakeRevenueSplitContract : IRevenueSplitContract
    {
        public readonly List<(Guid OrderId, IReadOnlyList<OrderItemSplitInfo> Items)> Recorded = [];
        public readonly List<(Guid OrderId, IReadOnlyList<Guid> OrderItemIds)> Reversed = [];

        public Task RecordRevenueSplitsAsync(Guid orderId, IReadOnlyList<OrderItemSplitInfo> items, CancellationToken cancellationToken)
        {
            Recorded.Add((orderId, items));
            return Task.CompletedTask;
        }

        public Task ReverseRevenueSplitsForOrderAsync(Guid orderId, IReadOnlyList<Guid> orderItemIds, CancellationToken cancellationToken)
        {
            Reversed.Add((orderId, orderItemIds));
            return Task.CompletedTask;
        }
    }


    private sealed class FakeEmailOutbox : IEmailOutbox
    {
        public readonly List<(string ToEmail, string Subject, string BodyHtml, string? TemplateKey)> Sent = [];

        public void Enqueue(string toEmail, string subject, string bodyHtml, string? templateKey) =>
            Sent.Add((toEmail, subject, bodyHtml, templateKey));
    }

    private sealed class FakeUserContactReader : IUserContactReader
    {
        public readonly Dictionary<Guid, string> Emails = [];

        public Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult(Emails.TryGetValue(userId, out var email) ? email : null);

        public Task<(string? Email, string? DisplayName)> GetUserContactInfoAsync(Guid userId, CancellationToken cancellationToken) =>
            Task.FromResult<(string?, string?)>((Emails.TryGetValue(userId, out var email) ? email : null, null));
    }
}
