using Siri.Modules.Live.Domain;

namespace Siri.Modules.Live.Application;

/// <summary>Persistence abstraction for <see cref="INSTRUCTOR_GOOGLE_ACCOUNT"/>, implemented by
/// <c>Infrastructure.InstructorGoogleAccountRepository</c>. Reads are tracked: the service mutates the aggregate through its
/// domain methods and saves.</summary>
public interface IInstructorGoogleAccountRepository
{
    /// <summary>The instructor's account row (active or revoked), tracked; <c>null</c> if they never connected.</summary>
    Task<INSTRUCTOR_GOOGLE_ACCOUNT?> GetByInstructorUserIdAsync(Guid instructorUserId, CancellationToken cancellationToken);

    /// <summary>User ids of the instructors whose connected account could be imported automatically (P11-13): an active credential, a stored Workspace domain and both
    /// recording scopes. A deliberately generous <em>pre-filter</em> (the scope test is a substring match; the exact decision is
    /// <see cref="RecordingCapabilityCalculator"/>'s) that lets discovery ask Catalog only about these instructors' sessions. Ids only, not tracked.</summary>
    Task<IReadOnlyList<Guid>> GetRecordingCandidateInstructorIdsAsync(CancellationToken cancellationToken);

    /// <summary>
    /// Records that a refresh-token exchange succeeded just now (<c>LAST_VALIDATED_AT_UTC</c>) <b>without leaving a pending change on the shared context</b>: a set-based
    /// write of that one column, which carries no row-version check and does not touch the change tracker's queue. The scoped <c>AppDbContext</c> is shared by every module
    /// of the request/job, so a tracked-but-unsaved edit of the account would be flushed by whatever saves next (Media's upload, a meeting row...) as an UPDATE with the
    /// row version this scope read - and lose to a parallel job that refreshed the same instructor's token, failing an unrelated operation with a concurrency exception.
    /// The tracked <paramref name="account"/> is brought up to date in memory (and left unmodified) so callers keep seeing the new value.
    /// </summary>
    Task RecordValidationAsync(INSTRUCTOR_GOOGLE_ACCOUNT account, DateTime validatedAtUtc, CancellationToken cancellationToken);

    void Add(INSTRUCTOR_GOOGLE_ACCOUNT account);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
