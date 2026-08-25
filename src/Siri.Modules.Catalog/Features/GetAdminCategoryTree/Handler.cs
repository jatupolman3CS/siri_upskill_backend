using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;

namespace Siri.Modules.Catalog.Features.GetAdminCategoryTree;

/// <summary>The admin category tree — includes inactive categories, deliberately not cached (low
/// traffic, and an admin editing the tree needs to see their own writes immediately, not up to
/// <see cref="CategoryTreeCache"/>'s TTL later).</summary>
public sealed class GetAdminCategoryTreeHandler(AppDbContext dbContext)
{
    public async Task<IReadOnlyList<CategoryTreeNode>> HandleAsync(CancellationToken cancellationToken)
    {
        var categories = await dbContext.Categories()
            .AsNoTracking()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return CategoryTreeAssembler.Assemble(categories, includeInactive: true);
    }
}
