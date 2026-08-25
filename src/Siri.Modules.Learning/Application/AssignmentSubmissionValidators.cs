using FluentValidation;
using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Application;

/// <summary>Format-only checks — see <see cref="CreateQuizRequestValidator"/>'s own doc comment.</summary>
public sealed class SubmitAssignmentRequestValidator : AbstractValidator<SubmitAssignmentRequest>
{
    public const int MaxStorageKeyLength = 500; // matches ASSIGNMENT_SUBMISSIONS.STORAGE_KEY's column width
    public const int MaxNoteLength = 2000; // matches ASSIGNMENT_SUBMISSIONS.NOTE's column width

    public SubmitAssignmentRequestValidator()
    {
        RuleFor(r => r.AssignmentId).NotEmpty();
        RuleFor(r => r.EnrollmentId).NotEmpty();
        RuleFor(r => r.StorageKey).NotEmpty().MaximumLength(MaxStorageKeyLength);
        RuleFor(r => r.Note).MaximumLength(MaxNoteLength);
    }
}

/// <summary>Format-only checks — see <see cref="CreateQuizRequestValidator"/>'s own doc comment.
/// <see cref="GradeAssignmentSubmissionRequest.Score"/>'s "required when Status is Graded" business rule
/// is deliberately NOT enforced here — same "format here, existence/business rules in the Service" split
/// every validator in this codebase follows; a format-only validator has no way to know the current
/// <see cref="AssignmentSubmissionStatus"/> transition rules beyond the shape of this one request.</summary>
public sealed class GradeAssignmentSubmissionRequestValidator : AbstractValidator<GradeAssignmentSubmissionRequest>
{
    public const int MaxFeedbackLength = 2000; // matches ASSIGNMENT_SUBMISSIONS.FEEDBACK's column width

    public GradeAssignmentSubmissionRequestValidator()
    {
        RuleFor(r => r.Status)
            .IsInEnum()
            .NotEqual(AssignmentSubmissionStatus.Submitted)
            .WithMessage("Status must be Graded or Rejected.");
        RuleFor(r => r.Score).InclusiveBetween(0, 100).When(r => r.Score is not null);
        RuleFor(r => r.Feedback).MaximumLength(MaxFeedbackLength);
    }
}
