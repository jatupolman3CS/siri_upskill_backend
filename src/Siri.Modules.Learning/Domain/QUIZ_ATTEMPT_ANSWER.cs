using Siri.SharedKernel;

namespace Siri.Modules.Learning.Domain;

/// <summary>
/// One recorded answer within a <see cref="QUIZ_ATTEMPT"/>. Construction is <c>internal</c>, reachable
/// only through <see cref="QUIZ_ATTEMPT.AddAnswer"/>.
/// <para>UPPERCASE naming exception — see <see cref="QUIZ"/>'s own doc comment.</para>
/// <para>Deliberately no <see cref="Siri.Persistence.Conventions.IAuditable"/> — same "pure child,
/// mutated only through its parent aggregate" reasoning as <see cref="QUIZ_QUESTION"/>'s own doc
/// comment.</para>
/// This is a SCAFFOLD pass — see <see cref="QUIZ"/>'s own doc comment for what that means for method
/// bodies here.
/// </summary>
public sealed class QUIZ_ATTEMPT_ANSWER
{
    /// <summary>EF Core materialization only.</summary>
    private QUIZ_ATTEMPT_ANSWER()
    {
    }

    public Guid QUIZ_ATTEMPT_ANSWER_ID { get; private set; }

    public Guid ATTEMPT_ID { get; private set; }

    /// <summary>FKs to <see cref="QUIZ_QUESTION.QUIZ_QUESTION_ID"/> conceptually — no database-level FK:
    /// the question lives on the separate <see cref="QUIZ"/> aggregate, and a cross-aggregate FK inside
    /// the same module is still exactly the kind of implicit coupling this codebase avoids at the
    /// database level (same instinct <c>QUIZ_ATTEMPT.QUIZ_ID</c>'s own doc comment applies, just between
    /// two aggregates in one module instead of two modules).</summary>
    public Guid QUESTION_ID { get; private set; }

    /// <summary>
    /// JSON-serialized array of the learner's selected <see cref="QUIZ_OPTION.QUIZ_OPTION_ID"/> values
    /// (e.g. <c>["...", "..."]</c>), stored as <c>nvarchar(max)</c> — a deliberate simplification already
    /// in the source schema sketch (docs/DATABASE.md: "SelectedOptionIds(json)"), not a real relational
    /// structure. This property holds the raw column value (a <see cref="string"/>), not a typed
    /// collection — serialization/deserialization is an Application-layer (Service) concern, kept out of
    /// Domain to avoid coupling this entity to a specific JSON library choice.
    /// </summary>
    public string SELECTED_OPTION_IDS { get; private set; } = string.Empty;

    public bool IS_CORRECT { get; private set; }

    /// <summary>Reachable only through <see cref="QUIZ_ATTEMPT.AddAnswer"/>.</summary>
    internal static QUIZ_ATTEMPT_ANSWER Create(Guid attemptId, Guid questionId, string selectedOptionIdsJson, bool isCorrect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedOptionIdsJson);

        return new QUIZ_ATTEMPT_ANSWER
        {
            QUIZ_ATTEMPT_ANSWER_ID = UuidV7.NewId(),
            ATTEMPT_ID = attemptId,
            QUESTION_ID = questionId,
            SELECTED_OPTION_IDS = selectedOptionIdsJson,
            IS_CORRECT = isCorrect,
        };
    }

    internal void Update(string selectedOptionIdsJson, bool isCorrect)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(selectedOptionIdsJson);
        SELECTED_OPTION_IDS = selectedOptionIdsJson;
        IS_CORRECT = isCorrect;
    }
}
