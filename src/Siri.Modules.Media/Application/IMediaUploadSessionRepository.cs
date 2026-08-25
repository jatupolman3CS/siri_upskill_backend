using Siri.Modules.Media.Domain;

namespace Siri.Modules.Media.Application;

/// <summary>Persistence port for <see cref="MEDIA_UPLOAD_SESSION"/> — see <see cref="IMediaAssetRepository"/>'s
/// own doc comment for why this lives here.</summary>
public interface IMediaUploadSessionRepository
{
    Task<MEDIA_UPLOAD_SESSION?> GetByIdAsync(Guid id, CancellationToken cancellationToken);

    void Add(MEDIA_UPLOAD_SESSION uploadSession);

    Task SaveChangesAsync(CancellationToken cancellationToken);
}
