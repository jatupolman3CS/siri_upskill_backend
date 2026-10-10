using Siri.SharedKernel;

namespace Siri.Integrations.Google;

/// <summary>Where Google says a recording is in its life cycle (Meet REST API <c>Recording.State</c>).</summary>
public enum MeetRecordingState
{
    /// <summary>Recording is still running or has just stopped; there is nothing to download yet (<c>STARTED</c>).</summary>
    Started,

    /// <summary>Recording finished but the file has not been generated yet (<c>ENDED</c>) — look again later.</summary>
    Ended,

    /// <summary>The MP4 exists in the organizer's Drive and can be downloaded (<c>FILE_GENERATED</c>).</summary>
    FileGenerated,
}

/// <summary>One recording of a Meet conference.</summary>
/// <param name="RecordingName">Meet resource name (<c>conferenceRecords/{id}/recordings/{id}</c>) — an id, not a secret.</param>
/// <param name="State">See <see cref="MeetRecordingState"/>.</param>
/// <param name="StartedAtUtc">When the recording started (UTC), when Google reported it.</param>
/// <param name="EndedAtUtc">When the recording ended (UTC), when Google reported it.</param>
/// <param name="DriveFileId">Drive file id of the MP4. <c>null</c> until <see cref="State"/> is <see cref="MeetRecordingState.FileGenerated"/>.</param>
public sealed record MeetRecording(
    string RecordingName,
    MeetRecordingState State,
    DateTime? StartedAtUtc,
    DateTime? EndedAtUtc,
    string? DriveFileId);

/// <summary>An open download of a Drive file. The caller owns it and must dispose it (disposing releases the HTTP response).</summary>
/// <param name="Content">The file body, streamed (never buffered whole — recordings can be several GB).</param>
/// <param name="ContentLength">Size in bytes when Google reported it.</param>
/// <param name="FileName">Drive's file name (display only — never used as a path).</param>
public sealed class MeetRecordingDownload(Stream content, long? contentLength, string? fileName, IDisposable? owner) : IDisposable
{
    public Stream Content { get; } = content;

    public long? ContentLength { get; } = contentLength;

    public string? FileName { get; } = fileName;

    public void Dispose()
    {
        Content.Dispose();
        owner?.Dispose();
    }
}

/// <summary>
/// Google Meet REST API (conference records and recordings) plus the Drive download of the recording file, acting for the
/// one instructor whose access token is supplied (P11-13 contract section 4). Needs the scopes
/// <see cref="GoogleScopes.MeetSpaceReadonly"/> and <see cref="GoogleScopes.DriveMeetReadonly"/>. Every expected failure is a typed
/// <see cref="Result"/> carrying a <see cref="GoogleErrors"/> code (a missing scope is <c>google.forbidden</c>, a revoked token
/// <c>google.unauthorized</c>); nothing throws for a Google/network failure. The meeting code and file ids are never logged.
/// </summary>
public interface IGoogleMeetRecordingProvider
{
    /// <summary>
    /// Finds the recordings of the conference(s) held in the Meet room <paramref name="meetingCode"/> (<c>abc-defg-hij</c>) that
    /// started inside [<paramref name="notBeforeUtc"/>, <paramref name="notAfterUtc"/>]. Success with an empty list = Google knows of
    /// no recording (yet). Oldest first.
    /// </summary>
    Task<Result<IReadOnlyList<MeetRecording>>> FindRecordingsAsync(
        string accessToken,
        string meetingCode,
        DateTime notBeforeUtc,
        DateTime notAfterUtc,
        CancellationToken ct);

    /// <summary>Opens the recording file for streaming. A Drive 404 is <c>google.not_found</c>.</summary>
    Task<Result<MeetRecordingDownload>> OpenDownloadAsync(string accessToken, string driveFileId, CancellationToken ct);
}
