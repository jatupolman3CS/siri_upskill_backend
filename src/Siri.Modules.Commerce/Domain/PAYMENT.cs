using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Domain;

/// <summary>
/// One payment attempt against an <see cref="ORDER"/> (docs/PAYMENT.md: Stripe, PromptPay QR via
/// PaymentIntent + webhook). An order can have more than one <c>PAYMENT</c> row over its lifetime
/// (retry after <see cref="PaymentStatus.Failed"/>/<see cref="PaymentStatus.Expired"/>), which is why
/// <see cref="ORDER_ID"/> is not unique.
/// <para>
/// Naming: SCREAMING_SNAKE_CASE class/properties, DB table/columns — the same brand-new-module
/// exception documented in full on <see cref="ORDER"/>'s doc comment (docs/DECISIONS.md D-17).
/// </para>
/// <para>
/// No <see cref="Siri.Persistence.Conventions.IAuditable"/>: docs/DATABASE.md's column sketch lists only
/// <see cref="CREATED_AT_UTC"/> (set once, in <see cref="Create"/>, via the <see cref="IClock"/> passed
/// in) — a payment attempt's later state transitions are tracked by <see cref="STATUS"/> plus the
/// explicit <see cref="SUCCEEDED_AT_UTC"/>/<see cref="FAILURE_REASON"/> fields, not a generic
/// <c>UpdatedAtUtc</c>.
/// </para>
/// <para>
/// No <see cref="Siri.Persistence.Conventions.ISoftDelete"/>: this is one of the four money tables this
/// scaffold pass applies database.md's "ห้าม hard delete ข้อมูลการเงิน" rule to (Orders, Payments,
/// Refunds, TaxInvoices) — there is deliberately no delete method at all; <see cref="STATUS"/> is the
/// only way this row's meaning changes after creation.
/// </para>
/// </summary>
public sealed class PAYMENT
{
    private PAYMENT()
    {
    }

    public Guid PAYMENT_ID { get; private set; }
    public Guid ORDER_ID { get; private set; }
    public PaymentMethod METHOD { get; private set; }

    /// <summary>Always <c>"Stripe"</c> for now — a string, not an enum, because docs/DECISIONS.md leaves
    /// room for a second provider later without a schema change (contrast <see cref="METHOD"/>, which
    /// *is* an enum because the set of payment methods Stripe itself exposes to us is the thing that's
    /// closed/known ahead of time).</summary>
    public string PROVIDER { get; private set; } = string.Empty;

    /// <summary>1:1 with a Stripe <c>PaymentIntent</c> id — this is the idempotency anchor for
    /// "did we already start a payment for this attempt", same role <see cref="STRIPE_WEBHOOK_EVENT.STRIPE_EVENT_ID"/>
    /// plays for inbound webhook delivery.</summary>
    public string PROVIDER_PAYMENT_INTENT_ID { get; private set; } = string.Empty;

    /// <summary>The amount actually sent to Stripe (and so the real money collected) — equals the order total
    /// unless an admin amount override was active when this attempt was created
    /// (<see cref="PAYMENT_AMOUNT_OVERRIDE"/>), in which case <see cref="ORIGINAL_AMOUNT"/> is set.</summary>
    public decimal AMOUNT { get; private set; }

    /// <summary>The order total this attempt WOULD have charged, set only when an admin amount override
    /// lowered <see cref="AMOUNT"/> below it; <c>null</c> for every normal payment (and for rows created
    /// before the override feature existed). Downstream money logic keys off this: revenue split pro-rates
    /// the instructors' gross to <see cref="AMOUNT"/>, and receipts/tax invoices (which print order-level
    /// totals) refuse to be issued for such a payment.</summary>
    public decimal? ORIGINAL_AMOUNT { get; private set; }

    public bool IsAmountOverridden => ORIGINAL_AMOUNT.HasValue;

    public PaymentStatus STATUS { get; private set; }
    public DateTime? SUCCEEDED_AT_UTC { get; private set; }
    public string? FAILURE_REASON { get; private set; }
    public DateTime CREATED_AT_UTC { get; private set; }

    public static PAYMENT Create(
        Guid orderId,
        PaymentMethod method,
        string providerPaymentIntentId,
        decimal amount,
        IClock clock,
        decimal? originalAmount = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(providerPaymentIntentId);
        ArgumentNullException.ThrowIfNull(clock);
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "amount must be positive.");
        if (originalAmount is { } original && original <= amount)
        {
            throw new ArgumentOutOfRangeException(nameof(originalAmount), original, "originalAmount must be greater than amount when provided (an override only lowers the charge).");
        }

        return new PAYMENT
        {
            PAYMENT_ID = UuidV7.NewId(), ORDER_ID = orderId, METHOD = method, PROVIDER = "Stripe",
            PROVIDER_PAYMENT_INTENT_ID = providerPaymentIntentId, AMOUNT = amount, ORIGINAL_AMOUNT = originalAmount,
            STATUS = PaymentStatus.Pending, CREATED_AT_UTC = clock.UtcNow,
        };
    }

    public void MarkProcessing()
    {
        if (STATUS != PaymentStatus.Pending) throw new InvalidOperationException($"Cannot mark a payment in {STATUS} status as processing.");
        STATUS = PaymentStatus.Processing;
    }

    public void MarkSucceeded(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (STATUS is not (PaymentStatus.Pending or PaymentStatus.Processing or PaymentStatus.Expired or PaymentStatus.Failed))
        {
            throw new InvalidOperationException($"Cannot mark a payment in {STATUS} status as succeeded.");
        }

        STATUS = PaymentStatus.Succeeded;
        SUCCEEDED_AT_UTC = clock.UtcNow;
    }

    public void MarkFailed(string failureReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
        if (STATUS is not (PaymentStatus.Pending or PaymentStatus.Processing))
        {
            throw new InvalidOperationException($"Cannot mark a payment in {STATUS} status as failed.");
        }

        STATUS = PaymentStatus.Failed;
        FAILURE_REASON = failureReason;
    }

    public void MarkExpired()
    {
        if (STATUS is not (PaymentStatus.Pending or PaymentStatus.Processing))
        {
            throw new InvalidOperationException($"Cannot mark a payment in {STATUS} status as expired.");
        }

        STATUS = PaymentStatus.Expired;
    }

    public void MarkRefunded(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (STATUS is not (PaymentStatus.Succeeded or PaymentStatus.Pending or PaymentStatus.Processing))
        {
            throw new InvalidOperationException($"Cannot mark a payment in {STATUS} status as refunded.");
        }

        STATUS = PaymentStatus.Refunded;
    }
}
