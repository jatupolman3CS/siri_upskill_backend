using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Infrastructure.Contracts;

/// <summary>Implementation of <see cref="IInstructorApprovalReader"/> — one indexed existence check on <c>InstructorProfiles.UserId</c> (unique).</summary>
public sealed class InstructorApprovalReader(AppDbContext dbContext) : IInstructorApprovalReader
{
    public async Task<bool> IsApprovedAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return false;
        }

        return await dbContext.InstructorProfiles()
            .AsNoTracking()
            .AnyAsync(p => p.UserId == userId && p.Status == InstructorApplicationStatus.Approved, cancellationToken)
            .ConfigureAwait(false);
    }
}
