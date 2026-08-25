using Siri.Persistence.Conventions;
using Siri.SharedKernel;

namespace Siri.Modules.Learning.Domain;

/// <summary>
/// One learner's file submission against an <see cref="ASSIGNMENT"/> — a separate small aggregate with
/// its own lifecycle (submit → grade/reject), not a child collection on <see cref="ASSIGNMENT"/> (see
/// that class's own doc comment for why).
/// <para>UPPERCASE naming exception — see <see cref="QUIZ"/>'s own doc comment.</para>
/// <para>
/// <see cref="ASSIGNMENT_ID"/> is a real FK (<c>NoAction</c> — cross-aggregate reference, same reasoning
/// as <see cref="QUIZ_ATTEMPT.QUIZ_ID"/>). <see cref="ENROLLMENT_ID"/> and <see cref="GRADED_BY_USER_ID"/>
/// have none — the former for the same "different aggregate/module, no repository contract for it yet in
/// this scaffold pass" reasoning as <see cref="QUIZ_ATTEMPT.ENROLLMENT_ID"/>; the latter because it
/// conceptually references <c>identity.Users.Id</c> (cross-module — same reasoning
/// <c>Catalog.Domain.InstructorProfile.UserId</c> already established for never getting a cross-module
/// FK).
/// </para>
/// This is a SCAFFOLD pass — see <see cref="QUIZ"/>'s own doc comment for what that means for method
/// bodies here.
/// </summary>
public sealed class ASSIGNMENT_SUBMISSION : IAuditable
{
    /// <summary>EF Core materialization only.</summary>
    private ASSIGNMENT_SUBMISSION()
    {
    }

    public Guid ASSIGNMENT_SUBMISSION_ID { get; private set; }

    public Guid ASSIGNMENT_ID { get; private set; }

    /// <summary>FKs to <c>learning.Enrollments.Id</c> conceptually — see this class's own doc comment for
    /// why there is no database-level FK constraint yet.</summary>
    public Guid ENROLLMENT_ID { get; private set; }

    /// <summary>Opaque key into whatever file storage backend a later task wires up (mirrors
    /// <c>media.MediaAssets</c>'s "provider-owned key, not a raw URL" shape) — never a directly-browsable
    /// URL; downloads should go through a signed URL per security.md's file-upload rule.</summary>
    public string STORAGE_KEY { get; private set; } = string.Empty;

    /// <summary>Optional note the learner attaches to their submission (e.g. "used approach B, see
    /// README").</summary>
    public string? NOTE { get; private set; }

    public DateTime SUBMITTED_AT_UTC { get; private set; }

    public AssignmentSubmissionStatus STATUS { get; private set; }

    /// <summary><c>null</c> until <see cref="Grade"/>. <c>decimal(5,2)</c> — same percentage-precision
    /// choice as <see cref="QUIZ_ATTEMPT.SCORE_PERCENT"/>, for the same reason (a later task decides the
    /// exact grading scale; this scaffold only fixes the column shape).</summary>
    public decimal? SCORE { get; private set; }

    /// <summary><c>null</c> until <see cref="Grade"/>/<see cref="Reject"/> — instructor's written
    /// feedback.</summary>
    public string? FEEDBACK { get; private set; }

    /// <summary>FKs to <c>identity.Users.Id</c> conceptually — see this class's own doc comment for why
    /// there is no database-level FK constraint.</summary>
    public Guid? GRADED_BY_USER_ID { get; private set; }

    public DateTime? GRADED_AT_UTC { get; private set; }

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

    /// <summary>Submits a new file for an assignment.</summary>
    public static ASSIGNMENT_SUBMISSION Submit(Guid assignmentId, Guid enrollmentId, string storageKey, string? note, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (assignmentId == Guid.Empty)
        {
            throw new ArgumentException("Assignment ID cannot be empty.", nameof(assignmentId));
        }

        if (enrollmentId == Guid.Empty)
        {
            throw new ArgumentException("Enrollment ID cannot be empty.", nameof(enrollmentId));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(storageKey);

        return new ASSIGNMENT_SUBMISSION
        {
            ASSIGNMENT_SUBMISSION_ID = UuidV7.NewId(),
            ASSIGNMENT_ID = assignmentId,
            ENROLLMENT_ID = enrollmentId,
            STORAGE_KEY = storageKey.Trim(),
            NOTE = note?.Trim(),
            SUBMITTED_AT_UTC = clock.UtcNow,
            STATUS = AssignmentSubmissionStatus.Submitted,
            CreatedAtUtc = clock.UtcNow,
        };
    }

    /// <summary>Grades a submitted submission.</summary>
    public void Grade(decimal score, string? feedback, Guid gradedByUserId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (gradedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Graded by user ID cannot be empty.", nameof(gradedByUserId));
        }

        SCORE = Math.Clamp(score, 0m, 100m);
        FEEDBACK = feedback?.Trim();
        GRADED_BY_USER_ID = gradedByUserId;
        GRADED_AT_UTC = clock.UtcNow;
        STATUS = AssignmentSubmissionStatus.Graded;
    }

    /// <summary>Rejects a submitted submission, sending it back to the learner.</summary>
    public void Reject(string? feedback, Guid gradedByUserId, IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (gradedByUserId == Guid.Empty)
        {
            throw new ArgumentException("Graded by user ID cannot be empty.", nameof(gradedByUserId));
        }

        FEEDBACK = feedback?.Trim();
        GRADED_BY_USER_ID = gradedByUserId;
        GRADED_AT_UTC = clock.UtcNow;
        STATUS = AssignmentSubmissionStatus.Rejected;
    }
}
