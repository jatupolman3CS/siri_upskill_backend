namespace Siri.Modules.Catalog.Contracts;

/// <summary>
/// <paramref name="AccessDurationDays"/> mirrors <c>COURSE.AccessDurationDays</c> — <c>null</c> means
/// lifetime access. Callers granting an enrollment (<c>Siri.Modules.Learning.Contracts
/// .ILearningAccessContract.EnrollUserAsync</c>) must use this to compute the enrollment's real
/// <c>EXPIRES_AT_UTC</c> — treating every purchase as lifetime access regardless of the course's actual
/// setting silently grants more than what was paid for.
/// </summary>
public sealed record CoursePriceInfo(Guid CourseId, string Title, decimal Price, Guid InstructorId, int? AccessDurationDays);

public sealed record CourseEpisodeInfo(Guid EpisodeId, Guid CourseId, string Title, int SortOrder);

public interface ICatalogPriceContract
{
    Task<IReadOnlyDictionary<Guid, CoursePriceInfo>> GetPublishedCoursePricesAsync(
        IEnumerable<Guid> courseIds,
        CancellationToken cancellationToken);

    Task<bool> IsEpisodeFreePreviewAsync(
        Guid episodeId,
        CancellationToken cancellationToken);

    Task<Guid?> GetCourseIdForEpisodeAsync(
        Guid episodeId,
        CancellationToken cancellationToken);

    Task<Guid?> GetMediaAssetIdForEpisodeAsync(
        Guid episodeId,
        CancellationToken cancellationToken) => Task.FromResult<Guid?>(null);

    /// <summary>
    /// True when <paramref name="instructorUserId"/> (an <c>identity.Users.Id</c>, e.g. from
    /// <c>IUserContext</c>) is the approved instructor owning the course that <paramref name="episodeId"/>
    /// belongs to.
    /// </summary>
    Task<bool> IsInstructorOwnerOfEpisodeAsync(
        Guid episodeId,
        Guid instructorUserId,
        CancellationToken cancellationToken);

    Task<bool> IsInstructorOwnerOfCourseAsync(
        Guid courseId,
        Guid instructorUserId,
        CancellationToken cancellationToken);

    Task<int> GetPendingReviewsCountAsync(CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, string>> GetCourseTitlesAsync(
        IEnumerable<Guid> courseIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyDictionary<Guid, decimal>> GetInstructorRevenueSharePercentsAsync(
        IEnumerable<Guid> instructorIds,
        CancellationToken cancellationToken);

    Task<IReadOnlyList<Guid>> GetCourseIdsByInstructorUserIdAsync(
        Guid instructorUserId,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<Guid>>(Array.Empty<Guid>());

    Task<IReadOnlyList<CourseEpisodeInfo>> GetEpisodesForCoursesAsync(
        IEnumerable<Guid> courseIds,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlyList<CourseEpisodeInfo>>(Array.Empty<CourseEpisodeInfo>());
}
