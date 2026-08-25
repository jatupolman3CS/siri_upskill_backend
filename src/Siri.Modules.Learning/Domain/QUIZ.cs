using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Learning.Domain;

/// <summary>
/// A quiz attached to a course episode, and the aggregate root for its <see cref="QUESTIONS"/> (each
/// owning its own <see cref="QUIZ_QUESTION.OPTIONS"/>) — see docs/DATABASE.md's "learning" section.
/// Composition mirrors <c>Siri.Modules.Catalog.Domain.Course</c>'s Sections/Episodes shape exactly: every
/// child is constructed only through this aggregate's own methods (<see cref="AddQuestion"/>, and
/// transitively <see cref="QUIZ_QUESTION.AddOption"/>), never independently.
/// <para>
/// <b>UPPERCASE naming exception</b>: per docs/DECISIONS.md D-17, this module is one of 7 new modules
/// that use Repository+Service instead of this repo's vertical-slice default (Identity/Catalog/
/// Notification stay untouched/PascalCase) and UPPERCASE entity class/property names + DB table/column
/// names. <see cref="IAuditable"/>'s four properties are the one deliberate exception — they stay
/// PascalCase in C# because <c>AuditableEntityInterceptor</c> looks them up via
/// <c>nameof(IAuditable.CreatedAtUtc)</c>, a hardcoded C#-member-name string lookup; only their mapped
/// *column* names go uppercase (see <c>QuizConfiguration</c>). See .claude/rules/backend.md and
/// .claude/rules/database.md for the full rule — this comment is not repeated on every entity in this
/// module.
/// </para>
/// <para>
/// A learner's quiz-taking flow (attempts, scoring, pass/fail) is <see cref="QUIZ_ATTEMPT"/> —
/// deliberately a separate aggregate, not a child collection here, because an attempt belongs to a
/// learner/enrollment, not to this quiz's own authoring lifecycle (same "separate small aggregate with
/// its own lifecycle" shape <c>Catalog.Domain.InstructorProfile</c> demonstrates for a different case).
/// </para>
/// <para>
/// Implemented for real as of 2026-08-24 (the old SCAFFOLD note here was stale). Correct-answer data
/// (<c>QUIZ_OPTION.IS_CORRECT</c>) must never reach a learner-facing response before submission — see
/// docs/TASKS.md P5-20.
/// </para>
/// </summary>
public sealed class QUIZ : IAuditable
{
    private readonly List<QUIZ_QUESTION> _questions = [];

    /// <summary>EF Core materialization only.</summary>
    private QUIZ()
    {
    }

    public Guid QUIZ_ID { get; private set; }

    /// <summary>FKs to <c>catalog.CourseEpisodes.Id</c> conceptually — no database-level FK constraint,
    /// ever: cross-module/cross-schema, same reasoning <c>Course.TrailerMediaAssetId</c> already
    /// established in Catalog (<c>CourseConfiguration</c>'s doc comment).</summary>
    public Guid EPISODE_ID { get; private set; }

    public string TITLE { get; private set; } = string.Empty;

    public int PASSING_SCORE_PERCENT { get; private set; }

    public int MAX_ATTEMPTS { get; private set; }

    public bool IS_ACTIVE { get; private set; }

    /// <summary>Mutate only through <see cref="AddQuestion"/> — never expose the backing list directly.</summary>
    public IReadOnlyCollection<QUIZ_QUESTION> QUESTIONS => _questions.AsReadOnly();

    // ---- IAuditable — stays PascalCase, see this class's own doc comment -------------------------
    public DateTime CreatedAtUtc { get; private set; }

    public Guid? CreatedBy { get; private set; }

    public DateTime? UpdatedAtUtc { get; private set; }

    public Guid? UpdatedBy { get; private set; }

    DateTime IAuditable.CreatedAtUtc
    {
        get => CreatedAtUtc;
        set => CreatedAtUtc = value;
    }

    Guid? IAuditable.CreatedBy
    {
        get => CreatedBy;
        set => CreatedBy = value;
    }

    DateTime? IAuditable.UpdatedAtUtc
    {
        get => UpdatedAtUtc;
        set => UpdatedAtUtc = value;
    }

    Guid? IAuditable.UpdatedBy
    {
        get => UpdatedBy;
        set => UpdatedBy = value;
    }

    /// <summary>Creates a new quiz for an episode.</summary>
    public static QUIZ Create(Guid episodeId, string title, int passingScorePercent, int maxAttempts)
    {
        if (episodeId == Guid.Empty)
        {
            throw new ArgumentException("Episode ID cannot be empty.", nameof(episodeId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(title);

        return new QUIZ
        {
            QUIZ_ID = UuidV7.NewId(),
            EPISODE_ID = episodeId,
            TITLE = title.Trim(),
            PASSING_SCORE_PERCENT = Math.Clamp(passingScorePercent, 0, 100),
            MAX_ATTEMPTS = Math.Max(1, maxAttempts),
            IS_ACTIVE = false,
        };
    }

    /// <summary>Appends a new question.</summary>
    public QUIZ_QUESTION AddQuestion(QuizQuestionType type, string text, string? explanation, int points)
    {
        var sortOrder = _questions.Count;
        var question = QUIZ_QUESTION.Create(QUIZ_ID, type, text, explanation, points, sortOrder);
        _questions.Add(question);
        return question;
    }

    /// <summary>Reassigns every question's SORT_ORDER.</summary>
    public void ReorderQuestions(IReadOnlyList<Guid> orderedQuestionIds)
    {
        ArgumentNullException.ThrowIfNull(orderedQuestionIds);

        for (var i = 0; i < orderedQuestionIds.Count; i++)
        {
            var q = _questions.FirstOrDefault(x => x.QUIZ_QUESTION_ID == orderedQuestionIds[i]);
            q?.Reorder(i);
        }
    }

    public void Activate()
    {
        IS_ACTIVE = true;
    }

    public void Deactivate()
    {
        IS_ACTIVE = false;
    }
}
