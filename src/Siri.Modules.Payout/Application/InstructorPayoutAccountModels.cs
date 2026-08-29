using FluentValidation;
using Siri.Modules.Payout.Domain;

namespace Siri.Modules.Payout.Application;

/// <summary>Request payload for POST /api/payout/instructor/payout-account.</summary>
public sealed record CreateInstructorPayoutAccountCommand(
    string BankCode,
    string AccountNo,
    string AccountName,
    string? TaxId,
    TaxPayerType TaxPayerType = TaxPayerType.Individual);

public sealed record InstructorPayoutAccountResponse(
    Guid Id,
    Guid InstructorId,
    string BankCode,
    string MaskedAccountNo,
    string AccountName,
    string? TaxId,
    TaxPayerType TaxPayerType,
    DateTime? VerifiedAtUtc);

/// <summary>Format-only checks — no DB access.</summary>
public sealed class CreateInstructorPayoutAccountValidator : AbstractValidator<CreateInstructorPayoutAccountCommand>
{
    public const int MaxBankCodeLength = 20;
    public const int MaxAccountNoLength = 34;
    public const int MaxAccountNameLength = 200;
    public const int MaxTaxIdLength = 20;

    public CreateInstructorPayoutAccountValidator()
    {
        RuleFor(c => c.BankCode).NotEmpty().MaximumLength(MaxBankCodeLength);
        RuleFor(c => c.AccountNo).NotEmpty().MaximumLength(MaxAccountNoLength);
        RuleFor(c => c.AccountName).NotEmpty().MaximumLength(MaxAccountNameLength);
        RuleFor(c => c.TaxId).MaximumLength(MaxTaxIdLength);
        RuleFor(c => c.TaxPayerType).IsInEnum();
    }
}
