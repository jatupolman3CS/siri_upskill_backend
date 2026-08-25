using Microsoft.EntityFrameworkCore;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Domain;
using Siri.Persistence;

namespace Siri.Modules.Learning.Infrastructure;

public sealed class AssignmentRepository(AppDbContext dbContext) : IAssignmentRepository
{
    public Task<ASSIGNMENT?> GetByIdAsync(Guid assignmentId, CancellationToken cancellationToken) =>
        dbContext.Assignments().FirstOrDefaultAsync(a => a.ASSIGNMENT_ID == assignmentId, cancellationToken);

    public Task<ASSIGNMENT?> GetByEpisodeIdAsync(Guid episodeId, CancellationToken cancellationToken) =>
        dbContext.Assignments().FirstOrDefaultAsync(a => a.EPISODE_ID == episodeId, cancellationToken);

    public void Add(ASSIGNMENT assignment) => dbContext.Assignments().Add(assignment);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
