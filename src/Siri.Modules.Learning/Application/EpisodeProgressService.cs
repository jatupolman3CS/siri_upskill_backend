using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Learning.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// Business logic for <see cref="Domain.EPISODE_PROGRESS"/>, backing <c>EpisodeProgressEndpoints</c>.
/// </summary>
public sealed class EpisodeProgressService(
    IEpisodeProgressRepository episodeProgressRepository,
    IEnrollmentRepository enrollmentRepository,
    IWatchEventRepository watchEventRepository,
    IClock clock,
    ICatalogPriceContract catalog)
{
    /// <summary>Creates or updates the caller's progress for one episode within one of their own enrollments.</summary>
    public async Task<Result<EpisodeProgressResponse>> UpsertProgressAsync(
        Guid userId,
        Guid enrollmentId,
        Guid episodeId,
        UpsertEpisodeProgressCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var enrollment = await enrollmentRepository.GetByIdAsync(enrollmentId, cancellationToken).ConfigureAwait(false);
        if (enrollment is null || enrollment.USER_ID != userId)
        {
            return Result.Failure<EpisodeProgressResponse>(DomainError.NotFound("ไม่พบข้อมูลการลงทะเบียนเรียน"));
        }

        if (enrollment.STATUS != EnrollmentStatus.Active || enrollment.EXPIRES_AT_UTC <= clock.UtcNow)
        {
            return Result.Failure<EpisodeProgressResponse>(DomainError.Forbidden("Enrollment is not active."));
        }

        var courseId = await catalog.GetCourseIdForEpisodeAsync(episodeId, cancellationToken).ConfigureAwait(false);
        if (courseId != enrollment.COURSE_ID)
        {
            return Result.Failure<EpisodeProgressResponse>(DomainError.NotFound("Episode does not belong to this enrollment."));
        }

        var progress = await episodeProgressRepository.GetByEnrollmentAndEpisodeAsync(
            enrollmentId, episodeId, cancellationToken).ConfigureAwait(false);

        var shouldUpdateProgress = progress is null
            || command.IsCompleted
            || Math.Abs(command.LastPositionSeconds - progress.LAST_POSITION_SECONDS) >= 10;

        EpisodeProgressResponse response;
        if (shouldUpdateProgress)
        {
            if (progress is null)
            {
                progress = EPISODE_PROGRESS.Create(
                    enrollmentId,
                    episodeId,
                    command.LastPositionSeconds,
                    command.WatchedSeconds,
                    command.IsCompleted,
                    clock);
                episodeProgressRepository.Add(progress);
            }
            else
            {
                progress.Touch(command.LastPositionSeconds, command.WatchedSeconds, command.IsCompleted, clock);
            }

            response = ToResponse(progress);
        }
        else
        {
            // Skip touching EPISODE_PROGRESS to keep heartbeat write volume down (docs/TASKS.md
            // X-29) -- but the caller must still see the position/watched seconds they just
            // reported, not the stale un-Touch()ed row, or a client that reloads right after a
            // skipped heartbeat sees resume position drift by up to the 10s skip window. Do NOT
            // call progress.Touch() here just to reuse ToResponse(): SaveChangesAsync below runs
            // unconditionally, so any mutation to this tracked entity would be persisted anyway,
            // defeating the whole point of skipping the write. IsCompleted/CompletedAtUtc always
            // come from the untouched entity: shouldUpdateProgress is already true whenever
            // command.IsCompleted is true, so this branch is only reached with
            // command.IsCompleted == false, and echoing that directly would incorrectly report
            // an already-completed episode as not completed.
            response = new EpisodeProgressResponse(
                progress!.EPISODE_PROGRESS_ID,
                progress.ENROLLMENT_ID,
                progress.EPISODE_ID,
                Math.Max(0, command.LastPositionSeconds),
                Math.Max(progress.WATCHED_SECONDS, command.WatchedSeconds),
                progress.IS_COMPLETED,
                progress.COMPLETED_AT_UTC,
                progress.UPDATED_AT_UTC);
        }

        var eventType = command.IsCompleted ? WatchEventType.Ended : WatchEventType.Heartbeat;
        var watchEvent = WATCH_EVENT.Create(
            enrollmentId,
            episodeId,
            eventType,
            command.LastPositionSeconds,
            clock);
        watchEventRepository.Append(watchEvent);

        await episodeProgressRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success(response);
    }

    /// <summary>Every progress row for one of the caller's own enrollments.</summary>
    public async Task<Result<IReadOnlyList<EpisodeProgressResponse>>> GetForEnrollmentAsync(
        Guid userId,
        Guid enrollmentId,
        CancellationToken cancellationToken)
    {
        var enrollment = await enrollmentRepository.GetByIdAsync(enrollmentId, cancellationToken).ConfigureAwait(false);
        if (enrollment is null || enrollment.USER_ID != userId)
        {
            return Result.Failure<IReadOnlyList<EpisodeProgressResponse>>(DomainError.NotFound("ไม่พบข้อมูลการลงทะเบียนเรียน"));
        }

        var progressList = await episodeProgressRepository.ListForEnrollmentAsync(
            enrollmentId, cancellationToken).ConfigureAwait(false);

        var mapped = progressList.Select(ToResponse).ToList();
        return Result.Success<IReadOnlyList<EpisodeProgressResponse>>(mapped);
    }

    private static EpisodeProgressResponse ToResponse(EPISODE_PROGRESS progress) =>
        new(
            progress.EPISODE_PROGRESS_ID,
            progress.ENROLLMENT_ID,
            progress.EPISODE_ID,
            progress.LAST_POSITION_SECONDS,
            progress.WATCHED_SECONDS,
            progress.IS_COMPLETED,
            progress.COMPLETED_AT_UTC,
            progress.UPDATED_AT_UTC);
}
