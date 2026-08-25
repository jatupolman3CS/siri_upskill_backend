using FluentValidation;

namespace Siri.Modules.Payout.Application;

/// <summary>Request payload for POST /api/payout/instructor/payout-account. Binds from the JSON request
/// body. <see cref="AccountNo"/> is the raw, plaintext bank account number as the instructor types it —
/// unlike the persisted <c>INSTRUCTOR_PAYOUT_ACCOUNT.ACCOUNT_NO_ENCRYPTED</c> column, this API boundary
/// type is honestly named for what it actually carries over the wire; encrypting it before it reaches the
/// domain entity is <see cref="InstructorPayoutAccountService"/>'s job (a later task — see that class's
/// own doc comment).</summary>
public sealed record CreateInstructorPayoutAccountCommand(string BankCode, string AccountNo, string AccountName, string? TaxId);

/// <summary>Deliberately never includes the account number, encrypted or not — see
/// <c>INSTRUCTOR_PAYOUT_ACCOUNT</c>'s own doc comment for why nothing about verifying/displaying "which
/// bank account is on file" requires echoing the number itself back to a client.</summary>
public sealed record InstructorPayoutAccountResponse(
    Guid Id,
    Guid InstructorId,
    string BankCode,
    string MaskedAccountNo,
    string AccountName,
    string? TaxId,
    DateTime? VerifiedAtUtc);

/// <summary>Format-only checks — no DB access (mirrors Catalog's <c>CreateCategoryValidator</c>'s own doc
/// comment: format here, existence/business rules in the service).</summary>
public sealed class CreateInstructorPayoutAccountValidator : AbstractValidator<CreateInstructorPayoutAccountCommand>
{
    public const int MaxBankCodeLength = 20; // matches INSTRUCTOR_PAYOUT_ACCOUNTS.BANK_CODE's column width
    public const int MaxAccountNoLength = 34; // generous upper bound for a raw (pre-encryption) account/IBAN-style number
    public const int MaxAccountNameLength = 200; // matches INSTRUCTOR_PAYOUT_ACCOUNTS.ACCOUNT_NAME's column width
    public const int MaxTaxIdLength = 20; // matches INSTRUCTOR_PAYOUT_ACCOUNTS.TAX_ID's column width

    public CreateInstructorPayoutAccountValidator()
    {
        RuleFor(c => c.BankCode).NotEmpty().MaximumLength(MaxBankCodeLength);
        RuleFor(c => c.AccountNo).NotEmpty().MaximumLength(MaxAccountNoLength);
        RuleFor(c => c.AccountName).NotEmpty().MaximumLength(MaxAccountNameLength);
        RuleFor(c => c.TaxId).MaximumLength(MaxTaxIdLength);
    }
}
