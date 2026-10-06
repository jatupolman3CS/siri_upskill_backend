using Siri.SharedKernel;

namespace Siri.Modules.Learning.Contracts;

/// <summary>
/// One course-enrollment grant within a batched <see cref="ILearningAccessContract.EnrollUserInCoursesAsync"/>
/// call — same per-grant shape as the individual parameters of <see cref="ILearningAccessContract.EnrollUserAsync"/>.
/// <see cref="ExpiresAtUtc"/> must be computed by the caller from the course's real
/// <c>AccessDurationDays</c> (via <c>Siri.Modules.Catalog.Contracts.ICatalogPriceContract.CoursePriceInfo</c>)
/// — <c>null</c> means lifetime access, not "unspecified."
/// </summary>
public sealed record CourseEnrollmentGrant(Guid CourseId, Guid? OrderId, DateTime? ExpiresAtUtc);

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
    /// Batched form of <see cref="HasActiveEnrollmentAsync"/> — which of <paramref name="courseIds"/> the
    /// user already holds an active (non-expired) enrollment for, checked in one query instead of one
    /// query per course. Added to fix a real N+1 on <c>OrderService.CreateAsync</c>'s duplicate-enrollment
    /// guard (it used to loop <see cref="HasActiveEnrollmentAsync"/> once per course in the order).
    /// Default implementation loops the single-course overload (correct, not batched) so any other
    /// implementer of this interface keeps compiling/behaving correctly without having to opt in — only
    /// <c>Siri.Modules.Learning.Infrastructure.Contracts.LearningAccessContract</c> overrides this with a
    /// real single-query implementation.
    /// </summary>
    async Task<IReadOnlySet<Guid>> HasActiveEnrollmentsAsync(
        Guid userId,
        IReadOnlyCollection<Guid> courseIds,
        CancellationToken cancellationToken)
    {
        var active = new HashSet<Guid>();
        foreach (var courseId in courseIds)
        {
            if (await HasActiveEnrollmentAsync(userId, courseId, cancellationToken).ConfigureAwait(false))
            {
                active.Add(courseId);
            }
        }

        return active;
    }

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

    /// <summary>
    /// Batched form of <see cref="EnrollUserAsync"/> — grants/reactivates enrollment for every course in
    /// <paramref name="grants"/> for the same user and <paramref name="source"/> in one round trip: one
    /// query to load this user's existing enrollments across the whole course set, an in-memory
    /// create-vs-reactivate decision per course (same rules as <see cref="EnrollUserAsync"/>), then one
    /// <c>SaveChangesAsync</c> for the whole batch. Added to fix a real N+1 on the Stripe
    /// payment-confirmation webhook and <c>OrderService.CreateAsync</c>'s 100%-discount instant-enrollment
    /// path, both of which used to loop <see cref="EnrollUserAsync"/> (one DB round trip + one
    /// <c>SaveChangesAsync</c> per course) for a multi-course order. Default implementation loops
    /// <see cref="EnrollUserAsync"/> (correct, not batched) so any other implementer of this interface
    /// keeps compiling/behaving correctly without having to opt in — only
    /// <c>Siri.Modules.Learning.Infrastructure.Contracts.LearningAccessContract</c> overrides this with the
    /// real batched implementation. <see cref="EnrollUserAsync"/> itself is kept (not removed) because
    /// other callers still enroll one course at a time (e.g. ops-resolution single-course grants).
    /// </summary>
    async Task<Result> EnrollUserInCoursesAsync(
        Guid userId,
        string source,
        IReadOnlyCollection<CourseEnrollmentGrant> grants,
        CancellationToken cancellationToken)
    {
        foreach (var grant in grants)
        {
            var result = await EnrollUserAsync(userId, grant.CourseId, grant.OrderId, source, grant.ExpiresAtUtc, cancellationToken).ConfigureAwait(false);
            if (result.IsFailure)
            {
                return result;
            }
        }

        return Result.Success();
    }

    /// <summary>
    /// User ids that hold an active/non-expired enrollment in <paramref name="courseId"/> right now — the
    /// same "active" rule <see cref="HasActiveEnrollmentAsync"/> uses (Status == Active AND (ExpiresAtUtc
    /// is null OR ExpiresAtUtc > now)), just returning the whole set for one course instead of checking
    /// one user at a time. Added for X-31 (deliver course announcements to actual enrolled learners — see
    /// <c>Siri.Modules.Notification.Contracts.IAnnouncementRecipientResolver</c>, consumed through
    /// <c>Siri.Modules.Learning.Infrastructure.Contracts.AnnouncementRecipientResolver</c>) and shaped so
    /// it can be reused by P11-04 (live-session invite job, docs/HYBRID_LIVE.md, still blocked on Q10/Q11)
    /// later — this method itself has nothing Live-specific about it and ships now without waiting on P11.
    /// Default implementation returns an empty set so any other implementer of this interface (e.g. a test
    /// double in another module) keeps compiling without having to opt in — only
    /// <c>Infrastructure.Contracts.LearningAccessContract</c> overrides this with a real query.
    /// </summary>
    Task<IReadOnlySet<Guid>> GetActiveEnrolledUserIdsAsync(
        Guid courseId,
        CancellationToken cancellationToken) => Task.FromResult<IReadOnlySet<Guid>>(new HashSet<Guid>());
}
