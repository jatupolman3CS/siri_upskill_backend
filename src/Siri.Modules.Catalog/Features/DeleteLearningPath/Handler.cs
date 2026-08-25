using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.DeleteLearningPath;

public sealed class DeleteLearningPathHandler(AppDbContext dbContext)
{
    public async Task<Result> HandleAsync(Guid id, CancellationToken cancellationToken)
    {
        var path = await dbContext.LearningPaths()
            .FirstOrDefaultAsync(p => p.Id == id, cancellationToken)
            .ConfigureAwait(false);

        if (path is null)
        {
            return Result.Failure(DomainError.NotFound("ไม่พบเส้นทางการเรียนที่ระบุ"));
        }

        dbContext.LearningPaths().Remove(path);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
