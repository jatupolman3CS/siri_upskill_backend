using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.Wishlist;

public sealed class RemoveFromWishlistHandler(AppDbContext dbContext)
{
    public async Task<Result> HandleAsync(
        Guid userId,
        Guid courseId,
        CancellationToken cancellationToken)
    {
        var item = await dbContext.Wishlists()
            .FirstOrDefaultAsync(w => w.UserId == userId && w.CourseId == courseId, cancellationToken)
            .ConfigureAwait(false);

        if (item is not null)
        {
            dbContext.Wishlists().Remove(item);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }

        return Result.Success();
    }
}
