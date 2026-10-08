namespace Siri.Modules.Catalog.Contracts;

/// <summary>
/// Resolves the instructor profile id of an authenticated user. Money tables (<c>REVENUE_SPLITS</c>, <c>PAYOUT_BATCH_ITEMS</c>,
/// <c>INSTRUCTOR_PAYOUT_ACCOUNTS</c>) key an instructor by <c>CATALOG.INSTRUCTOR_PROFILES.Id</c> (it is what <c>Course.InstructorId</c> carries),
/// while an HTTP caller is identified by the <b>user</b> id from <c>IUserContext</c> — this contract is the one place that maps one to the other, so
/// a caller can only ever reach the money of the profile that belongs to their own account.
/// </summary>
public interface IInstructorProfileReader
{
    /// <summary>
    /// The id of the instructor profile owned by <paramref name="userId"/> (any application status), or <c>null</c> when the user has no profile
    /// (including <see cref="Guid.Empty"/>). Read-only; never throws for an unknown user.
    /// </summary>
    Task<Guid?> GetProfileIdByUserIdAsync(Guid userId, CancellationToken cancellationToken);
}
