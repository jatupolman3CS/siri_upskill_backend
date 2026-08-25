using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Domain;

/// <summary>
/// A raw inbound Stripe webhook delivery, recorded before it is interpreted. <see cref="STRIPE_EVENT_ID"/>
/// is the idempotency guard — Stripe can and does redeliver the same event, and this table's unique
/// index on that column (see <c>STRIPE_WEBHOOK_EVENTConfiguration</c>) is what makes "have we already
/// processed this one" a single indexed lookup instead of something re-derived from <see cref="PAYMENT"/>
/// state (security.md: "Webhook ต้อง idempotent (unique index บน provider event id)").
/// <para>
/// Naming: SCREAMING_SNAKE_CASE class/properties, DB table/columns — the same brand-new-module
/// exception documented in full on <see cref="ORDER"/>'s doc comment (docs/DECISIONS.md D-17).
/// </para>
/// <para>
/// No <see cref="Siri.Persistence.Conventions.IAuditable"/>/<see cref="Siri.Persistence.Conventions.ISoftDelete"/>:
/// this is an append-only delivery log, not a domain aggregate with an audit trail or a deletable
/// resource — <see cref="RECEIVED_AT_UTC"/>/<see cref="PROCESSED_AT_UTC"/> already say everything about
/// this row's lifecycle that matters.
/// </para>
/// <para>
/// Scaffold scope: entity + EF config + repository only, no Service/Endpoints — a later task's webhook
/// handler (an unauthenticated, signature-verified endpoint per security.md) writes to this table
/// directly as part of verifying+recording the delivery, it is not its own CRUD resource a client calls.
/// </para>
/// </summary>
public sealed class STRIPE_WEBHOOK_EVENT
{
    private STRIPE_WEBHOOK_EVENT()
    {
    }

    public Guid STRIPE_WEBHOOK_EVENT_ID { get; private set; }
    public string STRIPE_EVENT_ID { get; private set; } = string.Empty;
    public string EVENT_TYPE { get; private set; } = string.Empty;

    /// <summary>Raw JSON payload, kept for dispute/audit replay — the one deliberate exception to
    /// database.md's "ทุกคอลัมน์ต้องกำหนด HasMaxLength()" rule in this whole scaffold pass. A Stripe
    /// event payload's size is provider-controlled and can legitimately be large (nested line items,
    /// metadata, expanded objects); truncating it with an arbitrary nvarchar length would silently
    /// corrupt the one copy of the record a payment dispute might need verbatim. See
    /// <c>STRIPE_WEBHOOK_EVENTConfiguration</c> for the <c>nvarchar(max)</c> mapping.</summary>
    public string PAYLOAD_JSON { get; private set; } = string.Empty;

    public DateTime RECEIVED_AT_UTC { get; private set; }
    public DateTime? PROCESSED_AT_UTC { get; private set; }
    public string? PROCESS_RESULT { get; private set; }

    public static STRIPE_WEBHOOK_EVENT Create(string stripeEventId, string eventType, string payloadJson, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(stripeEventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentException.ThrowIfNullOrWhiteSpace(payloadJson);
        ArgumentNullException.ThrowIfNull(clock);

        return new STRIPE_WEBHOOK_EVENT
        {
            STRIPE_WEBHOOK_EVENT_ID = UuidV7.NewId(), STRIPE_EVENT_ID = stripeEventId, EVENT_TYPE = eventType,
            PAYLOAD_JSON = payloadJson, RECEIVED_AT_UTC = clock.UtcNow,
        };
    }

    /// <summary>Records the outcome of processing this delivery. <paramref name="processResult"/> is a
    /// short human/log-facing summary (e.g. "payment.marked_succeeded" or an error code) — deciding its
    /// exact vocabulary belongs to whichever later task writes the webhook handler that calls this.</summary>
    public void MarkProcessed(string? processResult, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        PROCESSED_AT_UTC = clock.UtcNow;
        PROCESS_RESULT = processResult;
    }
}
