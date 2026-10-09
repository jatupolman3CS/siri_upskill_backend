using Siri.Modules.Payout.Domain;

namespace Siri.Modules.Payout.Application;

/// <summary>
/// Data access for <see cref="INSTRUCTOR_PAYOUT_ACCOUNT"/>, consumed by
/// <see cref="InstructorPayoutAccountService"/>.
/// </summary>
public interface IInstructorPayoutAccountRepository
{
    Task<INSTRUCTOR_PAYOUT_ACCOUNT?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>Tracked lookup by the unique <see cref="INSTRUCTOR_PAYOUT_ACCOUNT.INSTRUCTOR_ID"/> — the
    /// "does this instructor already have an account on file" / "my account" read path.</summary>
    Task<INSTRUCTOR_PAYOUT_ACCOUNT?> GetByInstructorIdAsync(Guid instructorId, CancellationToken cancellationToken);

    /// <summary>Untracked lookup of verified accounts for the specified instructors.</summary>
    Task<IReadOnlyDictionary<Guid, INSTRUCTOR_PAYOUT_ACCOUNT>> GetVerifiedAccountsAsync(IEnumerable<Guid> instructorIds, CancellationToken cancellationToken);

    /// <summary>Untracked (<c>AsNoTracking</c>) query source for read scenarios — the caller composes its
    /// own filtering/paging/projection.</summary>
    IQueryable<INSTRUCTOR_PAYOUT_ACCOUNT> Query();

    /// <summary>Stages a new row for insertion — does not persist until <see cref="SaveChangesAsync"/>.</summary>
    void Add(INSTRUCTOR_PAYOUT_ACCOUNT account);

    /// <summary>Stops tracking an account that was staged with <see cref="Add"/> but never persisted (its insert lost a race on the unique instructor key),
    /// so the next <see cref="SaveChangesAsync"/> does not try to insert it again.</summary>
    void Discard(INSTRUCTOR_PAYOUT_ACCOUNT account);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
