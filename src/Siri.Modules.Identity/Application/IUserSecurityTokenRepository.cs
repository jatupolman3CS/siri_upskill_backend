using Siri.Modules.Identity.Domain;

namespace Siri.Modules.Identity.Application;

public interface IUserSecurityTokenRepository
{
    Task<USER_SECURITY_TOKEN?> GetValidTokenAsync(Guid userId, UserSecurityTokenPurpose purpose, string tokenHash, CancellationToken cancellationToken);

    Task AddAsync(USER_SECURITY_TOKEN token, CancellationToken cancellationToken);

    Task UpdateAsync(USER_SECURITY_TOKEN token, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
