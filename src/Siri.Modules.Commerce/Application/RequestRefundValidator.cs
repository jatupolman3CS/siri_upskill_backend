using FluentValidation;

namespace Siri.Modules.Commerce.Application;

/// <summary>Format-only checks — no DB access (mirrors Siri.Modules.Catalog's <c>CreateCourseValidator</c>
/// doc comment: format here, refund-eligibility business rules in <see cref="RefundService.RequestAsync"/>
/// once it is implemented).</summary>
public sealed class RequestRefundValidator : AbstractValidator<RequestRefundCommand>
{
    public RequestRefundValidator()
    {
        RuleFor(c => c.PaymentId).NotEmpty();
        RuleFor(c => c.Amount).GreaterThan(0);
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(1000);
    }
}
