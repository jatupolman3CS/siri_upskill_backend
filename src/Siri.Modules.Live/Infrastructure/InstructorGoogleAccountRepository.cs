using Microsoft.EntityFrameworkCore;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.Persistence;

namespace Siri.Modules.Live.Infrastructure;

public sealed class InstructorGoogleAccountRepository(AppDbContext context) : IInstructorGoogleAccountRepository
{
    public Task<INSTRUCTOR_GOOGLE_ACCOUNT?> GetByInstructorUserIdAsync(Guid instructorUserId, CancellationToken cancellationToken) =>
        context.InstructorGoogleAccounts()
            .FirstOrDefaultAsync(a => a.INSTRUCTOR_USER_ID == instructorUserId, cancellationToken);

    public void Add(INSTRUCTOR_GOOGLE_ACCOUNT account) => context.InstructorGoogleAccounts().Add(account);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => context.SaveChangesAsync(cancellationToken);
}
