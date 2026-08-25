namespace Siri.Modules.Learning.Application;

/// <summary>
/// Request payload for POST /api/learning/quiz-attempts. Carries <see cref="EnrollmentId"/> explicitly
/// for this scaffold pass — a real implementation should resolve+verify the caller's own enrollment for
/// <see cref="QuizId"/>'s course server-side (from <see cref="Siri.SharedKernel.IUserContext.UserId"/>)
/// rather than trust it from the client outright, the same "userId มาจาก IUserContext เท่านั้น" principle
/// backend.md applies to instructor identity in Catalog's <c>CreateCourseCommand</c>. This module has no
/// Enrollment repository/contract wired up yet in this scaffold pass (Enrollment is being scaffolded in
/// parallel by another task) — <see cref="QuizAttemptService.StartAsync"/>'s own doc comment flags this
/// as a must-fix before this endpoint is real.
/// </summary>
public sealed record StartQuizAttemptRequest(Guid QuizId, Guid EnrollmentId);

/// <summary>Request payload for POST /api/learning/quiz-attempts/{id}/answers.</summary>
public sealed record SubmitQuizAnswerRequest(Guid QuestionId, IReadOnlyList<Guid> SelectedOptionIds);

public sealed record QuizAttemptResponse(
    Guid Id,
    Guid QuizId,
    Guid EnrollmentId,
    int AttemptNo,
    decimal? ScorePercent,
    bool IsPassed,
    DateTime StartedAtUtc,
    DateTime? SubmittedAtUtc,
    IReadOnlyList<QuizAttemptAnswerResponse> Answers);

public sealed record QuizAttemptAnswerResponse(Guid Id, Guid QuestionId, IReadOnlyList<Guid> SelectedOptionIds, bool IsCorrect);
