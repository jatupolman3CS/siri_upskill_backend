using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// Request payload for POST /api/learning/assignment-submissions. Carries <see cref="EnrollmentId"/>
/// explicitly for this scaffold pass — same "must be resolved+verified server-side from IUserContext,
/// not trusted from the client outright" caveat as <see cref="StartQuizAttemptRequest"/>'s own doc
/// comment; <see cref="AssignmentSubmissionService.SubmitAsync"/>'s own doc comment flags this as a
/// must-fix before this endpoint is real.
/// </summary>
public sealed record SubmitAssignmentRequest(Guid AssignmentId, Guid EnrollmentId, string StorageKey, string? Note);

/// <summary>Request payload for POST /api/learning/instructor/assignment-submissions/{id}/grade.
/// <see cref="Status"/> must be <see cref="AssignmentSubmissionStatus.Graded"/> or
/// <see cref="AssignmentSubmissionStatus.Rejected"/> — never <see cref="AssignmentSubmissionStatus.Submitted"/>
/// (that is the pre-grading state, not something this action can set it back to).</summary>
public sealed record GradeAssignmentSubmissionRequest(AssignmentSubmissionStatus Status, decimal? Score, string? Feedback);

public sealed record AssignmentSubmissionResponse(
    Guid Id,
    Guid AssignmentId,
    Guid EnrollmentId,
    string StorageKey,
    string? Note,
    DateTime SubmittedAtUtc,
    AssignmentSubmissionStatus Status,
    decimal? Score,
    string? Feedback,
    Guid? GradedByUserId,
    DateTime? GradedAtUtc);
