using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Application;

/// <summary>Request payload for POST /api/learning/instructor/quizzes. Deliberately carries no
/// caller/instructor id — resolved from <see cref="Siri.SharedKernel.IUserContext.UserId"/> by the
/// endpoint, never accepted from the client (backend.md's "userId มาจาก IUserContext เท่านั้น").</summary>
public sealed record CreateQuizRequest(Guid EpisodeId, string Title, int PassingScorePercent, int MaxAttempts);

/// <summary>Request payload for POST /api/learning/instructor/quizzes/{id}/questions.</summary>
public sealed record AddQuizQuestionRequest(QuizQuestionType Type, string Text, string? Explanation, int Points);

/// <summary>Request payload for POST /api/learning/instructor/quizzes/{quizId}/questions/{questionId}/options.</summary>
public sealed record AddQuizOptionRequest(string Text, bool IsCorrect);

/// <summary>
/// Full quiz projection, including every question/option — used by the instructor-authoring surface. A
/// later task building the learner-facing "take this quiz" screen MUST NOT reuse this shape as-is: it
/// exposes <see cref="QuizOptionResponse.IsCorrect"/>, which would leak the answer key to a learner
/// mid-attempt (security.md: only ownership/role gating is not enough — the response shape itself must
/// not carry data the caller shouldn't see). This scaffold pass does not build that separate,
/// answer-key-free projection — flagging it here so it is not missed.
/// </summary>
public sealed record QuizResponse(
    Guid Id,
    Guid EpisodeId,
    string Title,
    int PassingScorePercent,
    int MaxAttempts,
    bool IsActive,
    IReadOnlyList<QuizQuestionResponse> Questions);

public sealed record QuizQuestionResponse(
    Guid Id,
    QuizQuestionType Type,
    string Text,
    string? Explanation,
    int Points,
    int SortOrder,
    IReadOnlyList<QuizOptionResponse> Options);

public sealed record QuizOptionResponse(Guid Id, string Text, bool IsCorrect, int SortOrder);
