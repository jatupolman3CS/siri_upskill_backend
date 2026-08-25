using Siri.SharedKernel;

namespace Siri.Modules.Learning.Contracts;

public interface ILearningAccessContract
{
    Task<bool> CanUserAccessEpisodeAsync(
        Guid userId,
        Guid episodeId,
        CancellationToken cancellationToken);

    Task<bool> HasActiveEnrollmentAsync(
        Guid userId,
        Guid courseId,
        CancellationToken cancellationToken);

    /// <summary>
    /// Grants (or reactivates) a course enrollment. <paramref name="expiresAtUtc"/> must be computed by
    /// the caller from the course's real <c>AccessDurationDays</c> (via
    /// <c>Siri.Modules.Catalog.Contracts.ICatalogPriceContract.CoursePriceInfo</c>) — <c>null</c> means
    /// lifetime access, not "unspecified." Passing the wrong value here silently grants more or less
    /// access than what was actually paid for.
    /// </summary>
    Task<Result> EnrollUserAsync(
        Guid userId,
        Guid courseId,
        Guid? orderId,
        string source,
        DateTime? expiresAtUtc,
        CancellationToken cancellationToken);
}
