using FluentValidation;

namespace Siri.Modules.Community.Application.Response;

/// <summary>Format-only checks — no DB access, same reasoning as <c>CreateDiscussionValidator</c>'s own
/// doc comment.</summary>
public sealed class CreateReportValidator : AbstractValidator<CreateReportCommand>
{
    public const int MaxReasonLength = 500; // matches REPORTS.REASON's column width (ReportConfiguration)

    public CreateReportValidator()
    {
        RuleFor(c => c.DiscussionId).NotEmpty();
        RuleFor(c => c.Reason).NotEmpty().MaximumLength(MaxReasonLength);
    }
}
