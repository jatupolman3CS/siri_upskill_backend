using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Infrastructure.Contracts;

/// <summary>Implementation of <see cref="IInstructorProfileReader"/> — one indexed read on <c>InstructorProfiles.UserId</c> (unique).</summary>
public sealed class InstructorProfileReader(AppDbContext dbContext) : IInstructorProfileReader
{
    public async Task<Guid?> GetProfileIdByUserIdAsync(Guid userId, CancellationToken cancellationToken)
    {
        if (userId == Guid.Empty)
        {
            return null;
        }

        return await dbContext.InstructorProfiles()
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }
}
