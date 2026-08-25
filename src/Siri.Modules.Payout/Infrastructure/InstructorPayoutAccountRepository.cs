using Microsoft.EntityFrameworkCore;
using Siri.Modules.Payout.Application;
using Siri.Modules.Payout.Domain;
using Siri.Persistence;

namespace Siri.Modules.Payout.Infrastructure;

public sealed class InstructorPayoutAccountRepository(AppDbContext dbContext) : IInstructorPayoutAccountRepository
{
    public Task<INSTRUCTOR_PAYOUT_ACCOUNT?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.InstructorPayoutAccounts().FirstOrDefaultAsync(a => a.INSTRUCTOR_PAYOUT_ACCOUNT_ID == id, cancellationToken);

    public Task<INSTRUCTOR_PAYOUT_ACCOUNT?> GetByInstructorIdAsync(Guid instructorId, CancellationToken cancellationToken) =>
        dbContext.InstructorPayoutAccounts().FirstOrDefaultAsync(a => a.INSTRUCTOR_ID == instructorId, cancellationToken);

    public IQueryable<INSTRUCTOR_PAYOUT_ACCOUNT> Query() => dbContext.InstructorPayoutAccounts().AsNoTracking();

    public void Add(INSTRUCTOR_PAYOUT_ACCOUNT account) => dbContext.InstructorPayoutAccounts().Add(account);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
