using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Application;

/// <summary>Persistence boundary for the <see cref="QUIZ_ATTEMPT"/> aggregate (a separate aggregate from
/// <see cref="QUIZ"/> — see that class's own doc comment). Same "Repository+Service" shape as
/// <see cref="IQuizRepository"/> — see that interface's own doc comment.</summary>
public interface IQuizAttemptRepository
{
    /// <summary>Loads an attempt with its full <see cref="QUIZ_ATTEMPT.ANSWERS"/>, or <c>null</c> if
    /// <paramref name="attemptId"/> does not exist.</summary>
    Task<QUIZ_ATTEMPT?> GetByIdAsync(Guid attemptId, CancellationToken cancellationToken);

    /// <summary>Every attempt a given enrollment has made at a given quiz, most recent first — the shape
    /// <c>QuizAttemptService.StartAsync</c> needs to compute the next <see cref="QUIZ_ATTEMPT.ATTEMPT_NO"/>
    /// and enforce <see cref="QUIZ.MAX_ATTEMPTS"/>.</summary>
    Task<IReadOnlyList<QUIZ_ATTEMPT>> ListByEnrollmentAndQuizAsync(Guid enrollmentId, Guid quizId, CancellationToken cancellationToken);

    /// <summary>Stages a new attempt for insertion — does not persist until <see cref="SaveChangesAsync"/>.</summary>
    void Add(QUIZ_ATTEMPT attempt);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
