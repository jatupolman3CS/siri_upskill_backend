using FluentValidation;
using Siri.Modules.Payout.Domain;

namespace Siri.Modules.Payout.Application;

/// <summary>Request payload for POST /api/payout/admin/batches. Binds from the JSON request body.
/// <see cref="PAYOUT_BATCH.TOTAL_AMOUNT"/> is deliberately not a field here — it is a computed total (see
/// that property's own doc comment), never client-supplied.</summary>
public sealed record CreatePayoutBatchCommand(string PeriodKey);

/// <summary>Includes the full <see cref="Items"/> line-item list — this module has no separate "batch
/// summary" projection yet (unlike Catalog's course list vs. detail split), since an admin opening a batch
/// needs to see every instructor's line to act on it.</summary>
public sealed record PayoutBatchResponse(
    Guid Id,
    string PeriodKey,
    decimal TotalAmount,
    PayoutBatchStatus Status,
    DateTime CreatedAtUtc,
    DateTime? ExecutedAtUtc,
    Guid? ExecutedByUserId,
    IReadOnlyList<PayoutBatchItemResponse> Items);

public sealed record PayoutBatchItemResponse(
    Guid Id,
    Guid InstructorId,
    decimal Amount,
    decimal WithholdingTaxAmount,
    decimal NetAmount,
    PayoutBatchItemStatus Status,
    string? TransferRef);

/// <summary>Format-only checks — no DB access (mirrors Catalog's <c>CreateCategoryValidator</c>'s own doc
/// comment: format here, existence/business rules in the service).</summary>
public sealed class CreatePayoutBatchValidator : AbstractValidator<CreatePayoutBatchCommand>
{
    /// <summary><c>'YYYY-MM'</c> — matches <c>PAYOUT_BATCH.PERIOD_KEY</c>'s <c>char(7)</c> column
    /// (<c>PayoutBatchConfiguration</c>), same pattern <c>CreateRevenueSplitValidator</c> uses.</summary>
    private const string PeriodKeyPattern = @"^\d{4}-(0[1-9]|1[0-2])$";

    public CreatePayoutBatchValidator()
    {
        RuleFor(c => c.PeriodKey)
            .NotEmpty()
            .Matches(PeriodKeyPattern)
                .WithMessage("PeriodKey ต้องอยู่ในรูปแบบ YYYY-MM (เช่น 2026-08)");
    }
}
