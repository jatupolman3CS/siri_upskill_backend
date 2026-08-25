using FluentValidation;

namespace Siri.Modules.Commerce.Application;

/// <summary>Format-only checks — no DB access. <see cref="ApproveRefundCommand.DecisionNote"/> is optional
/// (mirrors <see cref="REFUND.Approve"/>'s own <c>string?</c> parameter), so this only bounds its length
/// when present.</summary>
public sealed class ApproveRefundValidator : AbstractValidator<ApproveRefundCommand>
{
    public ApproveRefundValidator()
    {
        RuleFor(c => c.DecisionNote).MaximumLength(1000);
    }
}
