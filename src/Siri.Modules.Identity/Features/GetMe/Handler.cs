using Microsoft.EntityFrameworkCore;
using Siri.Modules.Identity.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Identity.Features.GetMe;

/// <summary>
/// Returns the authenticated caller's own profile (<see cref="MeResponse"/>) — the data behind the
/// header avatar + name. The query filters on <see cref="GetMeQuery.UserId"/> directly (always
/// <see cref="IUserContext.UserId"/>, see that record's doc comment), so no code path can ever return a
/// row belonging to anyone else. Read-only, hence <c>AsNoTracking</c>; the existence/status decision
/// lives in <see cref="UserMeMappingExtensions.ToMeResult"/> so it can be unit-tested without a database.
/// </summary>
public sealed class GetMeHandler(AppDbContext dbContext)
{
    public async Task<Result<MeResponse>> HandleAsync(GetMeQuery query, CancellationToken cancellationToken)
    {
        var user = await dbContext.Users()
            .AsNoTracking()
            .Include(u => u.Roles)
            .FirstOrDefaultAsync(u => u.Id == query.UserId, cancellationToken)
            .ConfigureAwait(false);

        return user.ToMeResult();
    }
}
