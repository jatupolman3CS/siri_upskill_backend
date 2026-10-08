using Siri.Modules.Live.Domain;

namespace Siri.Modules.Live.Application;

/// <summary>Persistence abstraction for <see cref="SESSION_MEETING"/>, implemented by
/// <c>Infrastructure.SessionMeetingRepository</c>. Methods say whether the returned entities are tracked.</summary>
public interface ISessionMeetingRepository
{
    /// <summary>The meeting of one session, <b>tracked</b>; <c>null</c> if none. Also finds a meeting that was
    /// <see cref="Add"/>-ed on the shared context but not saved yet (the sink stages rows before the Catalog handler saves).</summary>
    Task<SESSION_MEETING?> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Meetings of the given sessions, <b>not tracked</b> (read-only snapshots for summaries, invites, reminders).</summary>
    Task<IReadOnlyList<SESSION_MEETING>> GetBySessionIdsAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken);

    /// <summary>Which of <paramref name="sessionIds"/> already have a meeting row (ids only, not tracked).</summary>
    Task<IReadOnlySet<Guid>> GetExistingSessionIdsAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken);

    /// <summary>Session ids of meetings the sync job should process now: <c>Pending</c>/<c>PendingDelete</c> whose retry time is
    /// unset or has passed, oldest first, at most <paramref name="take"/>. Ids only — the job loads each meeting tracked itself.</summary>
    Task<IReadOnlyList<Guid>> GetDueSessionIdsAsync(DateTime nowUtc, int take, CancellationToken cancellationToken);

    /// <summary>This instructor's meetings blocked on their Google account or on a link: <c>AwaitingLink</c>/<c>NeedsReconnect</c>/
    /// <c>Failed</c> with no room URL, <b>tracked</b>. Reset to <c>Pending</c> when they reconnect Google.</summary>
    Task<IReadOnlyList<SESSION_MEETING>> GetResettableByInstructorAsync(Guid instructorUserId, CancellationToken cancellationToken);

    void Add(SESSION_MEETING meeting);

    /// <summary>Commits everything pending on the shared context.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Forgets every tracked entity (after a failed unit of work) so the next one starts from the database state.</summary>
    void ClearTracking();
}
