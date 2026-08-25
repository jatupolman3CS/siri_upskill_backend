using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Learning.Domain;

/// <summary>
/// One learner's attempt at a <see cref="QUIZ"/> — a separate small aggregate from <see cref="QUIZ"/>
/// itself (see <see cref="QUIZ"/>'s own doc comment for why: an attempt belongs to a learner/enrollment's
/// lifecycle, not the quiz-authoring one). Owns <see cref="ANSWERS"/> — <see cref="QUIZ_ATTEMPT_ANSWER"/>
/// construction is only reachable through <see cref="AddAnswer"/> here.
/// <para>UPPERCASE naming exception — see <see cref="QUIZ"/>'s own doc comment.</para>
/// <para>
/// <see cref="QUIZ_ID"/> is a real FK (<c>NoAction</c> — cross-aggregate reference; this attempt does not
/// own the quiz's lifecycle, so it must not cascade-delete it). <see cref="ENROLLMENT_ID"/> has none —
/// <c>Enrollment</c> is a different aggregate within this same module, scaffolded in parallel by another
/// task; there is no cross-aggregate repository contract for it yet in this scaffold pass. Recording the
/// id only (same "no FK, just record the id" shape <c>Catalog.Domain.InstructorProfile.UserId</c> uses
/// for a cross-*module* id) is deliberate here for a cross-*aggregate*, same-module id instead — a later
/// task should decide whether that stays this way or gets tightened once Enrollment's own repository
/// exists.
/// </para>
/// This is a SCAFFOLD pass — see <see cref="QUIZ"/>'s own doc comment for what that means for method
/// bodies here.
/// </summary>
public sealed class QUIZ_ATTEMPT : IAuditable
{
    private readonly List<QUIZ_ATTEMPT_ANSWER> _answers = [];

    /// <summary>EF Core materialization only.</summary>
    private QUIZ_ATTEMPT()
    {
    }

    public Guid QUIZ_ATTEMPT_ID { get; private set; }

    public Guid QUIZ_ID { get; private set; }

    /// <summary>FKs to <c>learning.Enrollments.Id</c> conceptually — see this class's own doc comment for
    /// why there is no database-level FK constraint yet.</summary>
    public Guid ENROLLMENT_ID { get; private set; }

    /// <summary>1-based attempt number for this <see cref="ENROLLMENT_ID"/>/<see cref="QUIZ_ID"/> pair —
    /// enforced unique together at the database level (see <c>QuizAttemptConfiguration</c>) and bounded by
    /// <see cref="QUIZ.MAX_ATTEMPTS"/> (a later task's business rule, not this scaffold's).</summary>
    public int ATTEMPT_NO { get; private set; }

    /// <summary><c>null</c> until <see cref="Submit"/>. <c>decimal(5,2)</c> — a percentage, not a raw
    /// point count, so it needs fractional precision unlike <see cref="QUIZ.PASSING_SCORE_PERCENT"/>'s
    /// integer threshold (same precision choice as <c>Catalog.Domain.InstructorProfile.RevenueSharePercent</c>).</summary>
    public decimal? SCORE_PERCENT { get; private set; }

    public bool IS_PASSED { get; private set; }

    public DateTime STARTED_AT_UTC { get; private set; }

    /// <summary><c>null</c> until <see cref="Submit"/>.</summary>
    public DateTime? SUBMITTED_AT_UTC { get; private set; }

    /// <summary>Mutate only through <see cref="AddAnswer"/> — never expose the backing list directly.</summary>
    public IReadOnlyCollection<QUIZ_ATTEMPT_ANSWER> ANSWERS => _answers.AsReadOnly();

    // ---- IAuditable — stays PascalCase, see QUIZ's own doc comment --------------------------------
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

    /// <summary>Starts a new attempt.</summary>
    public static QUIZ_ATTEMPT Start(Guid quizId, Guid enrollmentId, int attemptNo, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (quizId == Guid.Empty)
        {
            throw new ArgumentException("Quiz ID cannot be empty.", nameof(quizId));
        }

        if (enrollmentId == Guid.Empty)
        {
            throw new ArgumentException("Enrollment ID cannot be empty.", nameof(enrollmentId));
        }

        return new QUIZ_ATTEMPT
        {
            QUIZ_ATTEMPT_ID = UuidV7.NewId(),
            QUIZ_ID = quizId,
            ENROLLMENT_ID = enrollmentId,
            ATTEMPT_NO = Math.Max(1, attemptNo),
            STARTED_AT_UTC = clock.UtcNow,
            CreatedAtUtc = clock.UtcNow,
        };
    }

    /// <summary>Records or updates one answer.</summary>
    public QUIZ_ATTEMPT_ANSWER AddAnswer(Guid questionId, string selectedOptionIdsJson, bool isCorrect)
    {
        var existing = _answers.FirstOrDefault(a => a.QUESTION_ID == questionId);
        if (existing is not null)
        {
            existing.Update(selectedOptionIdsJson, isCorrect);
            return existing;
        }

        var answer = QUIZ_ATTEMPT_ANSWER.Create(QUIZ_ATTEMPT_ID, questionId, selectedOptionIdsJson, isCorrect);
        _answers.Add(answer);
        return answer;
    }

    /// <summary>Finalizes the attempt with a score.</summary>
    public void Submit(decimal scorePercent, bool isPassed, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        SCORE_PERCENT = Math.Clamp(scorePercent, 0m, 100m);
        IS_PASSED = isPassed;
        SUBMITTED_AT_UTC = clock.UtcNow;
    }
}
