using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Domain;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Infrastructure;

public sealed class InstructorProfileRepository(AppDbContext dbContext) : IInstructorProfileRepository
{
    public Task<INSTRUCTOR_PROFILE?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.InstructorProfiles().FirstOrDefaultAsync(p => p.Id == id, cancellationToken);

    public Task<INSTRUCTOR_PROFILE?> GetByUserIdAsync(Guid userId, CancellationToken cancellationToken) =>
        dbContext.InstructorProfiles().FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken);

    public async Task<IReadOnlyList<INSTRUCTOR_PROFILE>> GetPendingApplicationsAsync(CancellationToken cancellationToken) =>
        await dbContext.InstructorProfiles()
            .Where(p => p.Status == InstructorApplicationStatus.Pending)
            .OrderBy(p => p.CreatedAtUtc)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

    public async Task AddAsync(INSTRUCTOR_PROFILE profile, CancellationToken cancellationToken)
    {
        dbContext.InstructorProfiles().Add(profile);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpdateAsync(INSTRUCTOR_PROFILE profile, CancellationToken cancellationToken)
    {
        dbContext.InstructorProfiles().Update(profile);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        dbContext.SaveChangesAsync(cancellationToken);
}
