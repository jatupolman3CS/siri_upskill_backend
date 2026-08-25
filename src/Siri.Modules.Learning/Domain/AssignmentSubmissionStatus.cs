namespace Siri.Modules.Learning.Domain;

/// <summary>
/// Lifecycle state of an <see cref="ASSIGNMENT_SUBMISSION"/>. Enum type name and members stay PascalCase
/// — only the property holding it (<see cref="ASSIGNMENT_SUBMISSION.STATUS"/>) goes uppercase — same
/// reasoning as <see cref="QuizQuestionType"/>'s own doc comment.
/// </summary>
public enum AssignmentSubmissionStatus
{
    /// <summary>Submitted, awaiting instructor review.</summary>
    Submitted,

    /// <summary>Reviewed and accepted, with a <see cref="ASSIGNMENT_SUBMISSION.SCORE"/>.</summary>
    Graded,

    /// <summary>Reviewed and sent back — <see cref="ASSIGNMENT_SUBMISSION.FEEDBACK"/> should explain why,
    /// same "a rejection with no reason attached is useless to the learner" reasoning
    /// <c>Course.RejectionReason</c> already established for course review in Catalog.</summary>
    Rejected,
}
