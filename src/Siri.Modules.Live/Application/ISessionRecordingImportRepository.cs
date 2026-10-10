using Siri.Modules.Live.Domain;

namespace Siri.Modules.Live.Application;

/// <summary>Persistence abstraction for <see cref="SESSION_RECORDING_IMPORT"/>, implemented by <c>Infrastructure.SessionRecordingImportRepository</c>.
/// Methods say whether the returned entities are tracked.</summary>
public interface ISessionRecordingImportRepository
{
    /// <summary>The import of one session, <b>tracked</b>; <c>null</c> if there is none. Also finds a row that was <see cref="Add"/>-ed on the shared
    /// context but not saved yet.</summary>
    Task<SESSION_RECORDING_IMPORT?> GetBySessionIdAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>One import by its own id, <b>tracked</b>; <c>null</c> if it does not exist.</summary>
    Task<SESSION_RECORDING_IMPORT?> GetByIdAsync(Guid importId, CancellationToken cancellationToken);

    /// <summary>The imports of the given sessions, <b>not tracked</b> (read-only snapshots for the instructor's lists).</summary>
    Task<IReadOnlyList<SESSION_RECORDING_IMPORT>> GetBySessionIdsAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken);

    /// <summary>Which of <paramref name="sessionIds"/> already have an import row (ids only, not tracked).</summary>
    Task<IReadOnlySet<Guid>> GetExistingSessionIdsAsync(IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken);

    /// <summary>Ids of the imports the job should work on now, oldest first, at most <paramref name="take"/>: <c>Waiting</c>/<c>Processing</c> whose
    /// <c>NEXT_ATTEMPT_AT_UTC</c> has passed, and <c>Transferring</c> whose lease has expired. Ids only — the job loads each row tracked itself.</summary>
    Task<IReadOnlyList<Guid>> GetDueIdsAsync(DateTime nowUtc, int take, CancellationToken cancellationToken);

    void Add(SESSION_RECORDING_IMPORT import);

    /// <summary>Commits everything pending on the shared context.</summary>
    Task SaveChangesAsync(CancellationToken cancellationToken);

    /// <summary>Forgets every tracked entity (after a failed unit of work) so the next one starts from the database state.</summary>
    void ClearTracking();
}
