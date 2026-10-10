using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Contracts;

/// <summary>A live session that has ended, as the recording import needs to see it.</summary>
/// <param name="SessionId">The live session.</param>
/// <param name="CourseId">Its course.</param>
/// <param name="InstructorUserId"><c>identity.Users.Id</c> of the course's instructor (the owner whose assets may be attached).</param>
/// <param name="Title">Session title (used to name the lesson).</param>
/// <param name="StartsAtUtc">Scheduled start.</param>
/// <param name="EndsAtUtc">Scheduled end.</param>
/// <param name="HasRecording">The session already has a recording lesson (uploaded by hand or imported).</param>
/// <param name="IsCancelled">The session was cancelled (a cancelled class has nothing to import).</param>
public sealed record EndedLiveSession(
    Guid SessionId,
    Guid CourseId,
    Guid InstructorUserId,
    string Title,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    bool HasRecording,
    bool IsCancelled);

/// <summary>What <see cref="ILiveRecordingAttacher.AttachAsync"/> created.</summary>
/// <param name="EpisodeId">The lesson that now holds the recording.</param>
public sealed record AttachedLiveRecording(Guid EpisodeId);

/// <summary>
/// Catalog's side of the automatic recording import (P11-13 contract section 5): which sessions have ended, and "make this ready media
/// asset the session's recording lesson". Implemented by Catalog over <c>AttachSessionRecordingHandler</c>, so every rule of the manual
/// attach (ownership, asset Ready, asset is the instructor's own, one asset one lesson, session started) applies unchanged.
/// </summary>
public interface ILiveRecordingAttacher
{
    /// <summary>Sessions whose end falls in [<paramref name="endedAfterUtc"/>, <paramref name="endedBeforeUtc"/>), oldest first, at most
    /// <paramref name="limit"/>. Cancelled sessions are included (flagged) so the caller can close them out. Sessions of a deleted or archived
    /// course are not listed (nothing can be attached to them).</summary>
    Task<IReadOnlyList<EndedLiveSession>> ListEndedAsync(DateTime endedAfterUtc, DateTime endedBeforeUtc, int limit, CancellationToken cancellationToken);

    /// <summary>
    /// Like <see cref="ListEndedAsync"/> but only the sessions taught by one of <paramref name="instructorUserIds"/> (<c>identity.Users.Id</c> of the course's
    /// instructor), same range, same oldest-first order, same inclusive start / exclusive end, at most <paramref name="limit"/>. The filter is part of the query so
    /// <paramref name="limit"/> counts only the sessions the caller cares about — a caller that filtered a plain <see cref="ListEndedAsync"/> page itself would
    /// never reach its own sessions once enough other instructors' sessions precede them. An empty set lists nothing.
    /// <para>
    /// The default implementation exists only so other implementers keep compiling: it filters a plain page in memory and therefore has that very starvation
    /// problem. <b>A real implementation must override it and push the filter into the query.</b>
    /// </para>
    /// </summary>
    async Task<IReadOnlyList<EndedLiveSession>> ListEndedByInstructorsAsync(
        IReadOnlyCollection<Guid> instructorUserIds,
        DateTime endedAfterUtc,
        DateTime endedBeforeUtc,
        int limit,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instructorUserIds);

        if (instructorUserIds.Count == 0)
        {
            return [];
        }

        var page = await ListEndedAsync(endedAfterUtc, endedBeforeUtc, limit, cancellationToken).ConfigureAwait(false);
        return page.Where(s => instructorUserIds.Contains(s.InstructorUserId)).ToList();
    }

    /// <summary>One session by id, or <c>null</c> when it does not exist (a session of a deleted or archived course reads as absent, like in
    /// <see cref="ListEndedAsync"/>).</summary>
    Task<EndedLiveSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken);

    /// <summary>Attaches <paramref name="mediaAssetId"/> as the recording of <paramref name="sessionId"/> on behalf of
    /// <paramref name="instructorUserId"/>. Idempotent for the same asset. Failures keep the manual handler's reasons, published as
    /// <see cref="LiveRecordingAttachReasons"/> (<c>live.recording_asset_not_ready</c>, <c>live.recording_asset_in_use</c>,
    /// <c>live.session_not_started</c>, ...); a missing/foreign course, session or asset is the handler's usual not-found / forbidden.</summary>
    Task<Result<AttachedLiveRecording>> AttachAsync(
        Guid instructorUserId,
        Guid courseId,
        Guid sessionId,
        Guid mediaAssetId,
        string? episodeTitle,
        CancellationToken cancellationToken);
}
