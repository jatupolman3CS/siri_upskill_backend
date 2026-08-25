using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Domain;

/// <summary>
/// An operator-facing triage entry for a <see cref="PAYMENT"/> that needs a human to look at it —
/// duplicate payment, payment received after expiry, refund stuck, etc. (docs/DATABASE.md's inline
/// examples for <see cref="REASON"/>). Effectively "one entry in the payment-ops queue" — the table
/// itself, per docs/DATABASE.md's sketch, is named <c>PaymentOpsQueue</c> (no trailing "s": it already
/// reads as a queue/collection, unlike e.g. <c>Payments</c>), so unlike this module's other entities
/// the mechanical singular-class/plural-table split doesn't apply — both the class and the table share
/// the same name, <c>PAYMENT_OPS_QUEUE</c>.
/// <para>
/// Naming: SCREAMING_SNAKE_CASE class/properties, DB table/columns — the same brand-new-module
/// exception documented in full on <see cref="ORDER"/>'s doc comment (docs/DECISIONS.md D-17).
/// </para>
/// <para>
/// No <see cref="Siri.Persistence.Conventions.IAuditable"/>/<see cref="Siri.Persistence.Conventions.ISoftDelete"/>:
/// docs/DATABASE.md's column sketch for this table has no created-at field at all (only
/// <see cref="RESOLVED_AT_UTC"/>) — followed literally here rather than adding one unrequested; a "when
/// was this queued" column would be a reasonable, purely-additive follow-up migration if a later task
/// needs it for ops triage.
/// </para>
/// <para>
/// Scaffold scope: entity + EF config + repository only, no Service/Endpoints — the admin resolve
/// workflow (assign, resolve, dismiss) is a later task, per this scaffold's own instructions.
/// </para>
/// </summary>
public sealed class PAYMENT_OPS_QUEUE
{
    private PAYMENT_OPS_QUEUE()
    {
    }

    public Guid PAYMENT_OPS_QUEUE_ID { get; private set; }
    public Guid PAYMENT_ID { get; private set; }
    public string REASON { get; private set; } = string.Empty;
    public PaymentOpsQueueStatus STATUS { get; private set; }

    /// <summary>Conceptual FK to <c>identity.Users.Id</c> — cross-module/schema, never a real DB FK
    /// constraint (same reasoning as every other cross-module user reference in this scaffold pass; see
    /// <see cref="ORDER.USER_ID"/>/<see cref="CART.USER_ID"/>).</summary>
    public Guid? ASSIGNED_TO_USER_ID { get; private set; }

    /// <summary>Conceptual FK to <c>identity.Users.Id</c> — same "no real FK" reasoning as
    /// <see cref="ASSIGNED_TO_USER_ID"/>.</summary>
    public Guid? RESOLVED_BY_USER_ID { get; private set; }

    public DateTime? RESOLVED_AT_UTC { get; private set; }
    public string? NOTE { get; private set; }

    public static PAYMENT_OPS_QUEUE Create(Guid paymentId, string reason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);

        return new PAYMENT_OPS_QUEUE
        {
            PAYMENT_OPS_QUEUE_ID = UuidV7.NewId(), PAYMENT_ID = paymentId, REASON = reason,
            STATUS = PaymentOpsQueueStatus.Open,
        };
    }

    public void Assign(Guid assignedToUserId)
    {
        if (STATUS != PaymentOpsQueueStatus.Open) throw new InvalidOperationException($"Cannot assign a queue entry in {STATUS} status.");

        STATUS = PaymentOpsQueueStatus.InProgress;
        ASSIGNED_TO_USER_ID = assignedToUserId;
    }

    public void Resolve(Guid resolvedByUserId, string? note, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        if (STATUS is PaymentOpsQueueStatus.Resolved or PaymentOpsQueueStatus.Dismissed)
        {
            throw new InvalidOperationException($"Cannot resolve a queue entry already in {STATUS} status.");
        }

        STATUS = PaymentOpsQueueStatus.Resolved;
        RESOLVED_BY_USER_ID = resolvedByUserId;
        RESOLVED_AT_UTC = clock.UtcNow;
        NOTE = note;
    }

    public void Dismiss(Guid resolvedByUserId, string note, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(note);
        ArgumentNullException.ThrowIfNull(clock);
        if (STATUS is PaymentOpsQueueStatus.Resolved or PaymentOpsQueueStatus.Dismissed)
        {
            throw new InvalidOperationException($"Cannot dismiss a queue entry already in {STATUS} status.");
        }

        STATUS = PaymentOpsQueueStatus.Dismissed;
        RESOLVED_BY_USER_ID = resolvedByUserId;
        RESOLVED_AT_UTC = clock.UtcNow;
        NOTE = note;
    }
}
