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
    IClock clock,
    ICourseEnrollmentCountUpdater courseEnrollmentCountUpdater) : ILearningAccessContract, IEpisodeAccessReader, ILearningEnrollmentChecker
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

    public async Task<IReadOnlySet<Guid>> HasActiveEnrollmentsAsync(
        Guid userId,
        IReadOnlyCollection<Guid> courseIds,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty || courseIds.Count == 0)
        {
            return new HashSet<Guid>();
        }

        // Same one-query batched lookup EnrollUserInCoursesAsync uses to decide create-vs-reactivate —
        // reused here (rather than a separate Query()-based projection) for the same reason
        // HasActiveEnrollmentAsync above reuses GetByUserAndCourseAsync: one repository method, one place
        // that knows how to look up this user's enrollments for a course set.
        var now = clock.UtcNow;
        var enrollments = await enrollmentRepository.GetByUserAndCoursesAsync(userId, courseIds, cancellationToken).ConfigureAwait(false);

        return enrollments
            .Where(e => e.STATUS == EnrollmentStatus.Active && (!e.EXPIRES_AT_UTC.HasValue || e.EXPIRES_AT_UTC.Value > now))
            .Select(e => e.COURSE_ID)
            .ToHashSet();
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
            if (existing.STATUS == EnrollmentStatus.Active &&
                (!existing.EXPIRES_AT_UTC.HasValue || existing.EXPIRES_AT_UTC.Value > clock.UtcNow))
            {
                // Idempotent no-op — e.g. a retried webhook delivery for the same order.
                return Result.Success();
            }

            // Expired or revoked: reactivate for the new purchase (progress is preserved by design —
            // see ENROLLMENT.Reactivate's own doc comment).
            var previousStatus = existing.STATUS;
            existing.Reactivate(orderId, expiresAtUtc, clock);
            await enrollmentRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

            // Runs inside the caller's transaction (a payment fulfilment): the count and the enrollment commit or roll back together.
            await EnrollmentCountRules.ReportAsync(courseEnrollmentCountUpdater, courseId, EnrollmentCountRules.DeltaForGrant(previousStatus), cancellationToken).ConfigureAwait(false);
            return Result.Success();
        }

        var parsedSource = Enum.TryParse<EnrollmentSource>(source, true, out var s) ? s : EnrollmentSource.Purchase;
        var enrollment = ENROLLMENT.Create(userId, courseId, orderId, parsedSource, expiresAtUtc, clock);
        enrollmentRepository.Add(enrollment);
        await enrollmentRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        await EnrollmentCountRules.ReportAsync(courseEnrollmentCountUpdater, courseId, EnrollmentCountRules.DeltaForGrant(previousStatus: null), cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }

    public async Task<Result> EnrollUserInCoursesAsync(
        Guid userId,
        string source,
        IReadOnlyCollection<CourseEnrollmentGrant> grants,
        CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return Result.Failure(DomainError.Validation("User ID cannot be empty."));
        }

        if (grants.Count == 0)
        {
            return Result.Success();
        }

        foreach (var grant in grants)
        {
            if (grant.CourseId == Guid.Empty)
            {
                return Result.Failure(DomainError.Validation("Course ID cannot be empty."));
            }
        }

        // One query for every existing enrollment across the whole course set (instead of one
        // GetByUserAndCourseAsync round trip per course), then decide create-vs-reactivate per course
        // entirely in memory — same rule EnrollUserAsync applies per-course, just batched.
        var courseIds = grants.Select(g => g.CourseId).ToList();
        var existingByCourseId = (await enrollmentRepository.GetByUserAndCoursesAsync(userId, courseIds, cancellationToken).ConfigureAwait(false))
            .ToDictionary(e => e.COURSE_ID);

        var parsedSource = Enum.TryParse<EnrollmentSource>(source, true, out var s) ? s : EnrollmentSource.Purchase;

        // Per-course change to the course's enrollment count (EnrollmentCountRules): only a real transition counts, so the idempotent no-op above never does.
        var countDeltas = new SortedDictionary<Guid, int>();

        foreach (var grant in grants)
        {
            if (existingByCourseId.TryGetValue(grant.CourseId, out var existing))
            {
                if (existing.STATUS == EnrollmentStatus.Active &&
                    (!existing.EXPIRES_AT_UTC.HasValue || existing.EXPIRES_AT_UTC.Value > clock.UtcNow))
                {
                    // Idempotent no-op for this course — e.g. a retried webhook delivery for the same order.
                    continue;
                }

                // Expired or revoked: reactivate for the new purchase (progress is preserved by design —
                // see ENROLLMENT.Reactivate's own doc comment).
                var previousStatus = existing.STATUS;
                existing.Reactivate(grant.OrderId, grant.ExpiresAtUtc, clock);
                AddDelta(countDeltas, grant.CourseId, EnrollmentCountRules.DeltaForGrant(previousStatus));
            }
            else
            {
                var enrollment = ENROLLMENT.Create(userId, grant.CourseId, grant.OrderId, parsedSource, grant.ExpiresAtUtc, clock);
                enrollmentRepository.Add(enrollment);
                AddDelta(countDeltas, grant.CourseId, EnrollmentCountRules.DeltaForGrant(previousStatus: null));
            }
        }

        // One SaveChangesAsync for the whole batch instead of one per course.
        await enrollmentRepository.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Inside the caller's transaction (a payment fulfilment): the counts and the enrollments commit or roll back together. Ascending course id — every
        // multi-course transaction takes the course rows in the same order, so two overlapping bundles can never deadlock on them.
        foreach (var (courseId, delta) in countDeltas)
        {
            await EnrollmentCountRules.ReportAsync(courseEnrollmentCountUpdater, courseId, delta, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }

    private static void AddDelta(SortedDictionary<Guid, int> deltas, Guid courseId, int delta) =>
        deltas[courseId] = deltas.GetValueOrDefault(courseId) + delta;

    public async Task<IReadOnlySet<Guid>> GetActiveEnrolledUserIdsAsync(Guid courseId, CancellationToken cancellationToken)
    {
        if (courseId == Guid.Empty)
        {
            return new HashSet<Guid>();
        }

        var now = clock.UtcNow;
        var userIds = await enrollmentRepository.Query()
            .Where(e => e.COURSE_ID == courseId
                && e.STATUS == EnrollmentStatus.Active
                && (!e.EXPIRES_AT_UTC.HasValue || e.EXPIRES_AT_UTC.Value > now))
            .Select(e => e.USER_ID)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return userIds.ToHashSet();
    }

    public async Task<IReadOnlyList<Guid>> GetActiveEnrolledCourseIdsAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return [];
        }

        // One query, the same "active" rule as HasActiveEnrollmentAsync/GetActiveEnrolledUserIdsAsync. UNIQUE (USER_ID, COURSE_ID) means no duplicates.
        var now = clock.UtcNow;
        return await enrollmentRepository.Query()
            .Where(e => e.USER_ID == userId
                && e.STATUS == EnrollmentStatus.Active
                && (!e.EXPIRES_AT_UTC.HasValue || e.EXPIRES_AT_UTC.Value > now))
            .Select(e => e.COURSE_ID)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
