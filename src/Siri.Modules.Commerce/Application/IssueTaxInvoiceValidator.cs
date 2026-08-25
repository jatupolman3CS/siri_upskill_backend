using FluentValidation;

namespace Siri.Modules.Commerce.Application;

/// <summary>Format-only checks — no DB access (mirrors Siri.Modules.Catalog's <c>CreateCourseValidator</c>
/// doc comment). <see cref="IssueTaxInvoiceCommand.TaxId"/>'s pattern is a Thai taxpayer identification
/// number: 13 digits (individual/juristic — same length either way), matching the plaintext length
/// <see cref="TAX_INVOICE.TAX_ID_ENCRYPTED"/>'s own doc comment describes ("~13-digit plaintext Thai tax
/// ID") before encryption is applied by a later task.</summary>
public sealed class IssueTaxInvoiceValidator : AbstractValidator<IssueTaxInvoiceCommand>
{
    private const string ThaiTaxIdPattern = @"^\d{13}$";

    public IssueTaxInvoiceValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.TaxId).NotEmpty().Matches(ThaiTaxIdPattern).WithMessage("เลขประจำตัวผู้เสียภาษีต้องเป็นตัวเลข 13 หลัก");
        RuleFor(c => c.BuyerName).NotEmpty().MaximumLength(200);
    }
}
