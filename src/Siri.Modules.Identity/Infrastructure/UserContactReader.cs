using Microsoft.EntityFrameworkCore;
using Siri.Modules.Identity.Contracts;
using Siri.Persistence;

namespace Siri.Modules.Identity.Infrastructure;

/// <summary>Implementation of <see cref="IUserContactReader"/> — see that interface's own doc comment
/// for the full contract.</summary>
public sealed class UserContactReader(AppDbContext dbContext) : IUserContactReader
{
    public async Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken)
    {
        return await dbContext.Users()
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => u.Email)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<(string? Email, string? DisplayName)> GetUserContactInfoAsync(Guid userId, CancellationToken cancellationToken)
    {
        var row = await dbContext.Users()
            .AsNoTracking()
            .Where(u => u.Id == userId)
            .Select(u => new { u.Email, u.DisplayName })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        return row is not null ? (row.Email, row.DisplayName) : (null, null);
    }
}
