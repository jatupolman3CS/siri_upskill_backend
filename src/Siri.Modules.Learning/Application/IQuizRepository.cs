using Siri.Modules.Learning.Domain;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// Persistence boundary for the <see cref="QUIZ"/> aggregate (docs/DECISIONS.md D-17 — this module uses
/// Repository+Service instead of this repo's vertical-slice default; see .claude/rules/backend.md).
/// Returns/accepts the full aggregate (with <see cref="QUIZ.QUESTIONS"/>/<see cref="QUIZ_QUESTION.OPTIONS"/>
/// loaded) — same "the aggregate root is the unit of persistence" shape Catalog's <c>Course</c> handlers
/// already use via a bare <c>AppDbContext</c>, just behind an explicit interface here instead.
/// </summary>
public interface IQuizRepository
{
    /// <summary>Loads a quiz with its full <see cref="QUIZ.QUESTIONS"/>/<see cref="QUIZ_QUESTION.OPTIONS"/>
    /// graph, or <c>null</c> if <paramref name="quizId"/> does not exist.</summary>
    Task<QUIZ?> GetByIdAsync(Guid quizId, CancellationToken cancellationToken);

    /// <summary>Loads the quiz attached to a given episode, or <c>null</c> if it has none.</summary>
    Task<QUIZ?> GetByEpisodeIdAsync(Guid episodeId, CancellationToken cancellationToken);

    /// <summary>Stages a new quiz for insertion — does not persist until <see cref="SaveChangesAsync"/>.</summary>
    void Add(QUIZ quiz);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
