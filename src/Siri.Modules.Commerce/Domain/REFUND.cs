using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Domain;

/// <summary>
/// A refund request against a <see cref="PAYMENT"/>, extended beyond docs/DATABASE.md's original
/// terse sketch per docs/DECISIONS.md D-17's gap-fill: the mockup handoff needed a real
/// request→decide→complete approval workflow (<see cref="DECIDED_BY_USER_ID"/>/<see cref="DECIDED_AT_UTC"/>/
/// <see cref="DECISION_NOTE"/>/<see cref="STRIPE_REFUND_ID"/> did not exist in the original column list),
/// backing the finance-report mockup's approve/reject-refund buttons.
/// <para>
/// Naming: SCREAMING_SNAKE_CASE class/properties, DB table/columns — the same brand-new-module
/// exception documented in full on <see cref="ORDER"/>'s doc comment (docs/DECISIONS.md D-17).
/// </para>
/// <para>
/// No <see cref="Siri.Persistence.Conventions.IAuditable"/>: this row's lifecycle is fully described by
/// its own named timestamps (<see cref="REQUESTED_AT_UTC"/>/<see cref="DECIDED_AT_UTC"/>/
/// <see cref="COMPLETED_AT_UTC"/>) plus <see cref="STATUS"/> — a generic Created/UpdatedAtUtc pair would
/// be redundant with, and less precise than, tracking each real lifecycle event has its own field.
/// </para>
/// <para>
/// No <see cref="Siri.Persistence.Conventions.ISoftDelete"/>: one of the four money tables this scaffold
/// pass applies database.md's "ห้าม hard delete ข้อมูลการเงิน" rule to (Orders, Payments, Refunds,
/// TaxInvoices) — <see cref="STATUS"/> is the only way this row's meaning changes, never a delete.
/// </para>
/// </summary>
public sealed class REFUND
{
    private REFUND()
    {
    }

    public Guid REFUND_ID { get; private set; }
    public Guid PAYMENT_ID { get; private set; }
    public decimal AMOUNT { get; private set; }

    /// <summary>The buyer's stated reason for wanting a refund — free text, not an enum (contrast
    /// <see cref="STATUS"/>): the set of reasons a learner might type is open-ended, unlike the closed
    /// workflow-stage set <see cref="RefundStatus"/> models.</summary>
    public string REASON { get; private set; } = string.Empty;

    public RefundStatus STATUS { get; private set; }

    /// <summary>Conceptual FK to <c>identity.Users.Id</c> — cross-module/schema, never a real DB FK
    /// constraint (same reasoning as <see cref="ORDER.USER_ID"/>).</summary>
    public Guid REQUESTED_BY_USER_ID { get; private set; }

    /// <summary>Renamed from docs/DATABASE.md's original sketch's bare <c>CreatedAtUtc</c> for clarity,
    /// per this scaffold task's own instructions — this is when the buyer asked, not when an admin
    /// decided anything.</summary>
    public DateTime REQUESTED_AT_UTC { get; private set; }

    /// <summary>Conceptual FK to <c>identity.Users.Id</c> — same "no real FK" reasoning as
    /// <see cref="REQUESTED_BY_USER_ID"/>. The admin who approved or rejected this request.</summary>
    public Guid? DECIDED_BY_USER_ID { get; private set; }

    public DateTime? DECIDED_AT_UTC { get; private set; }

    /// <summary>The admin's own reason for approving/rejecting — distinct from the buyer's
    /// <see cref="REASON"/> above.</summary>
    public string? DECISION_NOTE { get; private set; }

    /// <summary>Set once Stripe actually executes the refund (<see cref="RefundStatus.Processing"/> →
    /// <see cref="RefundStatus.Completed"/>) — unique when set (see <c>REFUNDConfiguration</c>'s filtered
    /// unique index), same idempotency-anchor role <see cref="PAYMENT.PROVIDER_PAYMENT_INTENT_ID"/> plays
    /// for the original payment.</summary>
    public string? STRIPE_REFUND_ID { get; private set; }

    public DateTime? COMPLETED_AT_UTC { get; private set; }

    public static REFUND Request(Guid paymentId, decimal amount, string reason, Guid requestedByUserId, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(clock);
        if (amount <= 0) throw new ArgumentOutOfRangeException(nameof(amount), amount, "amount must be positive.");

        return new REFUND
        {
            REFUND_ID = UuidV7.NewId(), PAYMENT_ID = paymentId, AMOUNT = amount, REASON = reason,
            STATUS = RefundStatus.Requested, REQUESTED_BY_USER_ID = requestedByUserId, REQUESTED_AT_UTC = clock.UtcNow,
        };
    }

    public void Approve(Guid decidedByUserId, string? decisionNote, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (STATUS != RefundStatus.Requested) throw new InvalidOperationException($"Cannot approve a refund in {STATUS} status.");

        STATUS = RefundStatus.Approved;
        DECIDED_BY_USER_ID = decidedByUserId;
        DECIDED_AT_UTC = clock.UtcNow;
        DECISION_NOTE = decisionNote;
    }

    public void Reject(Guid decidedByUserId, string decisionNote, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(decisionNote);
        ArgumentNullException.ThrowIfNull(clock);
        if (STATUS != RefundStatus.Requested) throw new InvalidOperationException($"Cannot reject a refund in {STATUS} status.");

        STATUS = RefundStatus.Rejected;
        DECIDED_BY_USER_ID = decidedByUserId;
        DECIDED_AT_UTC = clock.UtcNow;
        DECISION_NOTE = decisionNote;
    }

    public void MarkProcessing()
    {
        if (STATUS != RefundStatus.Approved) throw new InvalidOperationException($"Cannot mark a refund in {STATUS} status as processing.");
        STATUS = RefundStatus.Processing;
    }

    public void MarkCompleted(string stripeRefundId, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stripeRefundId);
        ArgumentNullException.ThrowIfNull(clock);
        if (STATUS != RefundStatus.Processing) throw new InvalidOperationException($"Cannot complete a refund in {STATUS} status.");

        STATUS = RefundStatus.Completed;
        STRIPE_REFUND_ID = stripeRefundId;
        COMPLETED_AT_UTC = clock.UtcNow;
    }

    public void MarkFailed()
    {
        if (STATUS != RefundStatus.Processing) throw new InvalidOperationException($"Cannot mark a refund in {STATUS} status as failed.");
        STATUS = RefundStatus.Failed;
    }
}
