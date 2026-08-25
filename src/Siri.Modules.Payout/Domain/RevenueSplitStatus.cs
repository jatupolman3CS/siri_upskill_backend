namespace Siri.Modules.Payout.Domain;

/// <summary>
/// Lifecycle of a <see cref="REVENUE_SPLIT"/> — docs/DATABASE.md's "payout" section: "Status —
/// Pending|Payable|Paid|Reversed". This is the status-enum-instead-of-delete mechanism
/// <see cref="REVENUE_SPLIT"/>'s own doc comment describes: <c>Pending</c> (just created, e.g. still
/// inside a refund window) → <c>Payable</c> (eligible to be picked up by a <see cref="PAYOUT_BATCH"/>) →
/// <c>Paid</c> (included in an <see cref="PayoutBatchStatus.Executed"/> batch) or <c>Reversed</c> (the
/// underlying order item was refunded/charged back — never deleted, the row stays as a permanent record).
/// The actual transition rules are a later task's business logic (see <see cref="REVENUE_SPLIT"/>'s doc
/// comment) — this scaffold only defines the states.
/// <para>
/// Type name and members stay PascalCase per D-17 — only the C# property that holds this enum
/// (<see cref="REVENUE_SPLIT.STATUS"/>) is uppercased, matching this module's naming exception for
/// enum-typed properties.
/// </para>
/// </summary>
public enum RevenueSplitStatus
{
    Pending,
    Payable,
    Paid,
    Reversed,
}
