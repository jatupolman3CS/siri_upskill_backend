using Siri.Modules.Learning.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// Business logic for <see cref="Domain.EPISODE_PROGRESS"/>, backing <c>EpisodeProgressEndpoints</c>.
/// </summary>
public sealed class EpisodeProgressService(
    IEpisodeProgressRepository episodeProgressRepository,
    IEnrollmentRepository enrollmentRepository,
    IClock clock)
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

        var progress = await episodeProgressRepository.GetByEnrollmentAndEpisodeAsync(
            enrollmentId, episodeId, cancellationToken).ConfigureAwait(false);

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

        await episodeProgressRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return Result.Success(ToResponse(progress));
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
