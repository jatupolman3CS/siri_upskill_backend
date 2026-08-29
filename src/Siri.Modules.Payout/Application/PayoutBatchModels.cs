using FluentValidation;
using Siri.Modules.Payout.Domain;

namespace Siri.Modules.Payout.Application;

/// <summary>Request payload for POST /api/payout/admin/batches. Binds from the JSON request body.</summary>
public sealed record CreatePayoutBatchCommand(string PeriodKey);

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
    decimal WithholdingTaxPercent,
    decimal WithholdingTaxAmount,
    decimal NetAmount,
    PayoutBatchItemStatus Status,
    string? TransferRef);

public sealed record BatchExportResponse(
    Guid BatchId,
    string PeriodKey,
    string FileName,
    string ContentType,
    string Content);

/// <summary>
/// Data model for Withholding Tax Certificate (หนังสือรับรองการหักภาษี ณ ที่จ่าย / 50 ทวิ) per docs/DECISIONS.md Q4.
/// </summary>
public sealed record WithholdingTaxCertificateResponse(
    Guid BatchItemId,
    string PeriodKey,
    DateTime IssuedAtUtc,
    string PayerName,
    string PayerTaxId,
    string PayerAddress,
    string PayeeName,
    string? PayeeTaxId,
    TaxPayerType TaxPayerType,
    string TaxFormType,
    decimal GrossIncomeAmount,
    decimal WithholdingTaxPercent,
    decimal WithholdingTaxAmount,
    decimal NetIncomeAmount);

/// <summary>Format-only checks — no DB access.</summary>
public sealed class CreatePayoutBatchValidator : AbstractValidator<CreatePayoutBatchCommand>
{
    private const string PeriodKeyPattern = @"^\d{4}-(0[1-9]|1[0-2])$";

    public CreatePayoutBatchValidator()
    {
        RuleFor(c => c.PeriodKey)
            .NotEmpty()
            .Matches(PeriodKeyPattern)
                .WithMessage("PeriodKey ต้องอยู่ในรูปแบบ YYYY-MM (เช่น 2026-08)");
    }
}
