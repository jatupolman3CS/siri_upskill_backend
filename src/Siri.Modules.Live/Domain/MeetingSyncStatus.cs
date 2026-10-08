namespace Siri.Modules.Live.Domain;

/// <summary>
/// Sync state of a <see cref="SESSION_MEETING"/> (docs/contracts/P11-03-live-module-google-meetings.md §2.3).
/// Enum type and members stay PascalCase (docs/DECISIONS.md D-17); stored as a string.
/// </summary>
public enum MeetingSyncStatus
{
    /// <summary>A Google event must be created/patched — the sync job picks the row up.</summary>
    Pending,

    /// <summary>The instructor has no connected Google account and no URL yet — waiting for a manual link.</summary>
    AwaitingLink,

    /// <summary>Calm. If a URL exists the room is ready.</summary>
    Synced,

    /// <summary>The instructor's Google token is unusable — needs a reconnect. A pre-existing URL stays valid.</summary>
    NeedsReconnect,

    /// <summary>Retries exhausted (five attempts) — the instructor can paste a link or request a resync.</summary>
    Failed,

    /// <summary>A Google event exists that must be deleted (session cancelled, or the instructor switched to a manual link).</summary>
    PendingDelete,

    /// <summary>Session cancelled / nothing left to do.</summary>
    Deleted,
}
