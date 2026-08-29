using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Learning.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Learning.Infrastructure.Contracts;

public sealed class LearningAccessContract(
    IEnrollmentRepository enrollmentRepository,
    ICatalogPriceContract catalogPriceContract,
    IClock clock) : ILearningAccessContract, IEpisodeAccessReader, ILearningEnrollmentChecker
{
    public async Task<bool> CanUserAccessEpisodeAsync(
        Guid userId,
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        if (episodeId == Guid.Empty)
        {
            return false;
        }

        // Check if episode is a free preview
        var isFreePreview = await catalogPriceContract.IsEpisodeFreePreviewAsync(episodeId, cancellationToken).ConfigureAwait(false);
        if (isFreePreview)
        {
            return true;
        }

        if (userId == Guid.Empty)
        {
            return false;
        }

        var courseId = await catalogPriceContract.GetCourseIdForEpisodeAsync(episodeId, cancellationToken).ConfigureAwait(false);
        if (courseId is null || courseId.Value == Guid.Empty)
        {
            return false;
        }

        return await HasActiveEnrollmentAsync(userId, courseId.Value, cancellationToken).ConfigureAwait(false);
    }

    public async Task<bool> HasActiveEnrollmentAsync(
        Guid userId,
        Guid courseId,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || courseId == Guid.Empty)
        {
            return false;
        }

        var enrollment = await enrollmentRepository.GetByUserAndCourseAsync(userId, courseId, cancellationToken).ConfigureAwait(false);
        if (enrollment is null)
        {
            return false;
        }

        if (enrollment.STATUS != EnrollmentStatus.Active)
        {
            return false;
        }

        if (enrollment.EXPIRES_AT_UTC.HasValue && enrollment.EXPIRES_AT_UTC.Value <= clock.UtcNow)
        {
            return false;
        }

        return true;
    }

    public async Task<Result> EnrollUserAsync(
        Guid userId,
        Guid courseId,
        Guid? orderId,
        string source,
        DateTime? expiresAtUtc,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure(DomainError.Validation("User ID cannot be empty."));
        }

        if (courseId == Guid.Empty)
        {
            return Result.Failure(DomainError.Validation("Course ID cannot be empty."));
        }

        var existing = await enrollmentRepository.GetByUserAndCourseAsync(userId, courseId, cancellationToken).ConfigureAwait(false);
        if (existing is not null)
        {
            if (existing.STATUS == EnrollmentStatus.Active)
            {
                // Idempotent no-op — e.g. a retried webhook delivery for the same order.
                return Result.Success();
            }

            // Expired or revoked: reactivate for the new purchase (progress is preserved by design —
            // see ENROLLMENT.Reactivate's own doc comment).
            existing.Reactivate(orderId, expiresAtUtc, clock);
            await enrollmentRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }

        var parsedSource = Enum.TryParse<EnrollmentSource>(source, true, out var s) ? s : EnrollmentSource.Purchase;
        var enrollment = ENROLLMENT.Create(userId, courseId, orderId, parsedSource, expiresAtUtc, clock);
        enrollmentRepository.Add(enrollment);
        await enrollmentRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
