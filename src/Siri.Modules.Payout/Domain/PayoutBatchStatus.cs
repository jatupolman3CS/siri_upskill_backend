namespace Siri.Modules.Payout.Domain;

/// <summary>
/// Lifecycle of a <see cref="PAYOUT_BATCH"/> — docs/DATABASE.md's "payout" section: "PayoutBatches(Id PK,
/// PeriodKey, TotalAmount, Status, CreatedAtUtc, ExecutedAtUtc, ExecutedBy)". <c>Draft</c> (created,
/// empty/being assembled) → <c>Executed</c> (transfers were run — see <see cref="PAYOUT_BATCH.EXECUTED_AT_UTC"/>/
/// <see cref="PAYOUT_BATCH.EXECUTED_BY_USER_ID"/>) or <c>Failed</c> (the run itself errored before/during
/// transfer — distinct from an individual instructor's transfer failing, which is a per-row concern on
/// <see cref="PayoutBatchItemStatus"/> instead, not this batch-level status). Populating
/// <see cref="PAYOUT_BATCH.Items"/> and actually executing transfers is later-task business logic (see
/// <see cref="PAYOUT_BATCH"/>'s own doc comment) — this scaffold only defines the states.
/// <para>
/// Type name and members stay PascalCase per D-17 — only the C# property that holds this enum
/// (<see cref="PAYOUT_BATCH.STATUS"/>) is uppercased.
/// </para>
/// </summary>
public enum PayoutBatchStatus
{
    Draft,
    Executed,
    Failed,
}
