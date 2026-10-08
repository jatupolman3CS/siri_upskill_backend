using Siri.Modules.Live.Domain;

namespace Siri.Modules.Live.Application;

/// <summary>
/// Persistence abstraction for the write side of <see cref="SESSION_INVITE"/> (docs/contracts/
/// P11-04-live-invites-ics-reminders.md §4.4/§4.5), implemented by <c>Infrastructure.SessionInviteRepository</c>. Everything
/// returned is <b>tracked</b> — the invite and reminder jobs mutate the invites and commit them in the same
/// <c>SaveChanges</c> as the outbox rows they stage. (The read-only view used by the learner/instructor queries is the separate
/// <see cref="ISessionInviteReader"/>.)
/// </summary>
public interface ISessionInviteRepository
{
    /// <summary>Every invite (any status) of the given sessions, <b>tracked</b>. Also returns an invite that was
    /// <see cref="Add"/>-ed on the shared context but not saved yet.</summary>
    Task<IReadOnlyList<SESSION_INVITE>> GetBySessionIdsAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken);

    /// <summary>The <see cref="InviteStatus.Invited"/> invites of the given sessions that still have at least one reminder
    /// (24 h or 1 h) to send, <b>tracked</b>.</summary>
    Task<IReadOnlyList<SESSION_INVITE>> GetInvitedAwaitingReminderAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken);

    /// <summary>Which of the given sessions have a <b>learner</b> invite whose Google attendee state differs from its invite state
    /// (P11-04 §6): an <see cref="InviteStatus.Invited"/> one not yet on the event, or a withdrawn/skipped one still stamped as on it.
    /// Session ids only, not tracked — the cheap "does anything need syncing" check; the sync loads the invites itself.</summary>
    Task<IReadOnlyList<Guid>> GetSessionIdsNeedingAttendeeSyncAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken);

    void Add(SESSION_INVITE invite);

    /// <summary>Commits everything pending on the shared context (invites, e-mail outbox rows, in-app notifications).</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Forgets every tracked/staged entity (after a failed unit of work) so nothing half-applied is ever saved and the next
    /// unit starts from the database state.</summary>
    void ClearTracking();
}
