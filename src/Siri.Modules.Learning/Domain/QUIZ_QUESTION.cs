using Siri.SharedKernel;

namespace Siri.Modules.Learning.Domain;

/// <summary>
/// One question inside a <see cref="QUIZ"/>. Construction is <c>internal</c>, reachable only through
/// <see cref="QUIZ.AddQuestion"/> — mirrors <c>Catalog.Domain.CourseSection</c>'s relationship to
/// <c>Course</c>. Owns <see cref="OPTIONS"/> — <see cref="QUIZ_OPTION"/> construction is only reachable
/// through <see cref="AddOption"/> here, never directly.
/// <para>UPPERCASE naming exception — see <see cref="QUIZ"/>'s own doc comment.</para>
/// <para>
/// Deliberately no <see cref="Siri.Persistence.Conventions.IAuditable"/> — a pure child, mutated only
/// through its parent aggregate (<see cref="QUIZ"/>); nothing ever reads/writes a question's own audit
/// timestamps independently of the quiz it belongs to. Unlike Catalog's <c>CourseSection</c> (which does
/// implement <c>IAuditable</c>), this is a deliberate choice specific to this module's cluster — see the
/// scaffold task notes this class was generated from.
/// </para>
/// This is a SCAFFOLD pass — see <see cref="QUIZ"/>'s own doc comment for what that means for method
/// bodies here.
/// </summary>
public sealed class QUIZ_QUESTION
{
    private readonly List<QUIZ_OPTION> _options = [];

    /// <summary>EF Core materialization only.</summary>
    private QUIZ_QUESTION()
    {
    }

    public Guid QUIZ_QUESTION_ID { get; private set; }

    public Guid QUIZ_ID { get; private set; }

    public QuizQuestionType TYPE { get; private set; }

    public string TEXT { get; private set; } = string.Empty;

    public string? EXPLANATION { get; private set; }

    public int POINTS { get; private set; }

    /// <summary>Display order among sibling questions within the same <see cref="QUIZ_ID"/>. Owned by
    /// <see cref="QUIZ.ReorderQuestions"/> only.</summary>
    public int SORT_ORDER { get; private set; }

    /// <summary>Mutate only through <see cref="AddOption"/> — never expose the backing list directly.</summary>
    public IReadOnlyCollection<QUIZ_OPTION> OPTIONS => _options.AsReadOnly();

    /// <summary>Reachable only through <see cref="QUIZ.AddQuestion"/>.</summary>
    internal static QUIZ_QUESTION Create(Guid quizId, QuizQuestionType type, string text, string? explanation, int points, int sortOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return new QUIZ_QUESTION
        {
            QUIZ_QUESTION_ID = UuidV7.NewId(),
            QUIZ_ID = quizId,
            TYPE = type,
            TEXT = text.Trim(),
            EXPLANATION = explanation?.Trim(),
            POINTS = Math.Max(1, points),
            SORT_ORDER = sortOrder,
        };
    }

    /// <summary>Appends a new option.</summary>
    public QUIZ_OPTION AddOption(string text, bool isCorrect)
    {
        var sortOrder = _options.Count;
        var option = QUIZ_OPTION.Create(QUIZ_QUESTION_ID, text, isCorrect, sortOrder);
        _options.Add(option);
        return option;
    }

    /// <summary>Called by <see cref="QUIZ.ReorderQuestions"/>.</summary>
    internal void Reorder(int sortOrder)
    {
        SORT_ORDER = sortOrder;
    }
}
