using FluentValidation;

namespace Siri.Modules.Commerce.Application;

/// <summary>Format-only checks — no DB access. Unlike <see cref="ApproveRefundCommand"/>,
/// <see cref="RejectRefundCommand.DecisionNote"/> is required (mirrors <see cref="REFUND.Reject"/>'s own
/// non-nullable <c>string</c> parameter — a rejection with no stated reason leaves the buyer with nothing
/// to act on).</summary>
public sealed class RejectRefundValidator : AbstractValidator<RejectRefundCommand>
{
    public RejectRefundValidator()
    {
        RuleFor(c => c.DecisionNote).NotEmpty().MaximumLength(1000);
    }
}
