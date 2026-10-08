using Siri.Modules.Live.Domain;

namespace Siri.Modules.Live.Application;

/// <summary>Persistence abstraction for <see cref="INSTRUCTOR_GOOGLE_ACCOUNT"/>, implemented by
/// <c>Infrastructure.InstructorGoogleAccountRepository</c>. Reads are tracked: the service mutates the aggregate through its
/// domain methods and saves.</summary>
public interface IInstructorGoogleAccountRepository
{
    /// <summary>The instructor's account row (active or revoked), tracked; <c>null</c> if they never connected.</summary>
    Task<INSTRUCTOR_GOOGLE_ACCOUNT?> GetByInstructorUserIdAsync(Guid instructorUserId, CancellationToken cancellationToken);

    void Add(INSTRUCTOR_GOOGLE_ACCOUNT account);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
