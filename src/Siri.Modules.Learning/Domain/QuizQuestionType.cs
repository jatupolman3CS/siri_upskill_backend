namespace Siri.Modules.Learning.Domain;

/// <summary>
/// Kind of a <see cref="QUIZ_QUESTION"/> — how many <see cref="QUIZ_OPTION"/>s a learner may select when
/// answering it.
/// <para>
/// Enum type name and members stay PascalCase even inside this otherwise-UPPERCASE-named module
/// (docs/DECISIONS.md D-17) — only the *property holding* the enum (<see cref="QUIZ_QUESTION.TYPE"/>)
/// goes uppercase. Members serialize into JSON via the global <c>JsonStringEnumConverter</c>
/// (<c>Siri.Api/Program.cs</c>'s <c>ConfigureHttpJsonOptions</c>) — changing member names/casing here
/// would change the wire contract, which is out of scope for this naming convention.
/// </para>
/// </summary>
public enum QuizQuestionType
{
    SingleChoice,
    MultipleChoice,
}
