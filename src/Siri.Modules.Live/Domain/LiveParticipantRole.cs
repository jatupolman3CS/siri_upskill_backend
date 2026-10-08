namespace Siri.Modules.Live.Domain;

/// <summary>
/// The capacity in which a user takes part in a live session — shared by <see cref="SESSION_INVITE"/> and
/// <see cref="SESSION_JOIN_LOG"/> (docs/contracts/P11-04-live-invites-ics-reminders.md §2.2,
/// P11-05-live-learner-instructor-api-join-gate.md §2). Refund (P11-12) and the dashboard KPIs count only
/// <see cref="Learner"/> rows. Enum type and members stay PascalCase (docs/DECISIONS.md D-17); stored as a string.
/// </summary>
public enum LiveParticipantRole
{
    /// <summary>A learner with an active enrollment in the session's course.</summary>
    Learner,

    /// <summary>The instructor who owns the session's course.</summary>
    Instructor,
}
