namespace Siri.Modules.Live.Domain;

/// <summary>
/// Where one session's automatic recording import stands (P11-13 contract section 3). Enum type and members stay PascalCase (D-17); stored as a string.
/// Terminal: <see cref="Attached"/>, <see cref="NoRecording"/>, <see cref="Failed"/>, <see cref="NeedsReconnect"/>, <see cref="Skipped"/>. Only
/// <see cref="Failed"/>, <see cref="NoRecording"/> and <see cref="NeedsReconnect"/> can be reset to <see cref="Waiting"/> by the instructor's retry.
/// </summary>
public enum RecordingImportStatus
{
    /// <summary>Looking for the recording in Google (re-polled with a backoff until the search window closes).</summary>
    Waiting,

    /// <summary>Copying the file from Google Drive to the video provider (held under a lease so a crashed run is reclaimed).</summary>
    Transferring,

    /// <summary>The video provider is transcoding the copy; the row waits for the media asset to become <c>Ready</c>.</summary>
    Processing,

    /// <summary>Done: the recording is a lesson of the course.</summary>
    Attached,

    /// <summary>The search window ended with no recording file → the instructor uploads by hand.</summary>
    NoRecording,

    /// <summary>Gave up (retries exhausted, file too large, transcode failed, ...) → upload by hand, or retry.</summary>
    Failed,

    /// <summary>Google no longer lets the platform read the recording (token revoked, scope missing) → the instructor must reconnect/consent.</summary>
    NeedsReconnect,

    /// <summary>Nothing to import: the session was cancelled, a recording lesson already exists, or there is no Google room.</summary>
    Skipped,
}
