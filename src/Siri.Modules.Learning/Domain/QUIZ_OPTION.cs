using Siri.SharedKernel;

namespace Siri.Modules.Learning.Domain;

/// <summary>
/// One answer choice for a <see cref="QUIZ_QUESTION"/>. Construction is <c>internal</c>, reachable only
/// through <see cref="QUIZ_QUESTION.AddOption"/>.
/// <para>UPPERCASE naming exception — see <see cref="QUIZ"/>'s own doc comment.</para>
/// <para>Deliberately no <see cref="Siri.Persistence.Conventions.IAuditable"/> — same "pure child,
/// mutated only through its parent aggregate" reasoning as <see cref="QUIZ_QUESTION"/>'s own doc comment,
/// one level further down the tree.</para>
/// This is a SCAFFOLD pass — see <see cref="QUIZ"/>'s own doc comment for what that means for method
/// bodies here.
/// </summary>
public sealed class QUIZ_OPTION
{
    /// <summary>EF Core materialization only.</summary>
    private QUIZ_OPTION()
    {
    }

    public Guid QUIZ_OPTION_ID { get; private set; }

    public Guid QUESTION_ID { get; private set; }

    public string TEXT { get; private set; } = string.Empty;

    /// <summary>Whether selecting this option counts toward a correct answer. Never exposed to a learner
    /// mid-attempt by a correctly-built later implementation — the caller of
    /// <see cref="QUIZ.QUESTIONS"/>/<see cref="OPTIONS"/> for the instructor-authoring surface needs it;
    /// whatever DTO a learner sees while *taking* a quiz must not.</summary>
    public bool IS_CORRECT { get; private set; }

    /// <summary>Display order among sibling options within the same <see cref="QUESTION_ID"/>. Owned by
    /// a future reorder method on <see cref="QUIZ_QUESTION"/> (not part of this scaffold pass — no
    /// acceptance criterion asked for option reordering specifically).</summary>
    public int SORT_ORDER { get; private set; }

    /// <summary>Reachable only through <see cref="QUIZ_QUESTION.AddOption"/>.</summary>
    internal static QUIZ_OPTION Create(Guid questionId, string text, bool isCorrect, int sortOrder)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(text);

        return new QUIZ_OPTION
        {
            QUIZ_OPTION_ID = UuidV7.NewId(),
            QUESTION_ID = questionId,
            TEXT = text.Trim(),
            IS_CORRECT = isCorrect,
            SORT_ORDER = sortOrder,
        };
    }
}
