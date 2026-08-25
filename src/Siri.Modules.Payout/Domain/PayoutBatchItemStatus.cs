namespace Siri.Modules.Payout.Domain;

/// <summary>
/// Lifecycle of one <see cref="PAYOUT_BATCH_ITEM"/> — docs/DATABASE.md's "payout" section only lists a
/// bare "Status" column for <c>PayoutBatchItems</c> without naming states, so this scaffold defines a
/// separate set from <see cref="PayoutBatchStatus"/> rather than reusing it: a batch's own status
/// describes whether the run happened at all, while a real payout run can plausibly transfer money to
/// some instructors successfully while a specific bank transfer to one other instructor fails (wrong
/// account number, bank API timeout, ...) — collapsing both concepts onto one enum would make "the batch
/// executed but this one instructor's money never moved" inexpressible. <c>Pending</c> (row created,
/// transfer not yet attempted) → <c>Transferred</c> (this instructor's money moved) or <c>Failed</c> (this
/// instructor's transfer specifically failed — the row stays, per this module's no-hard-delete rule, so
/// ops has a permanent record to retry/investigate).
/// <para>
/// Type name and members stay PascalCase per D-17 — only the C# property that holds this enum
/// (<see cref="PAYOUT_BATCH_ITEM.STATUS"/>) is uppercased.
/// </para>
/// </summary>
public enum PayoutBatchItemStatus
{
    Pending,
    Transferred,
    Failed,
}
