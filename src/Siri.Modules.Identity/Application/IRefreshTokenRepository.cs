using Siri.Modules.Identity.Domain;

namespace Siri.Modules.Identity.Application;

public interface IRefreshTokenRepository
{
    Task<REFRESH_TOKEN?> GetByTokenHashAsync(string tokenHash, CancellationToken cancellationToken);

    Task AddAsync(REFRESH_TOKEN token, CancellationToken cancellationToken);

    Task UpdateAsync(REFRESH_TOKEN token, CancellationToken cancellationToken);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
