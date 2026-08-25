using FluentValidation;

namespace Siri.Modules.Commerce.Application;

/// <summary>Format-only checks — no DB access (mirrors Siri.Modules.Catalog's <c>CreateCourseValidator</c>
/// doc comment).</summary>
public sealed class CreatePaymentValidator : AbstractValidator<CreatePaymentCommand>
{
    public CreatePaymentValidator()
    {
        RuleFor(c => c.OrderId).NotEmpty();
        RuleFor(c => c.Method).IsInEnum();
    }
}
