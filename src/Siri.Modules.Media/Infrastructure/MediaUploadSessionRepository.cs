using Microsoft.EntityFrameworkCore;
using Siri.Modules.Media.Application;
using Siri.Modules.Media.Domain;
using Siri.Persistence;

namespace Siri.Modules.Media.Infrastructure;

/// <summary>EF Core-backed <see cref="IMediaUploadSessionRepository"/> — see
/// <see cref="MediaAssetRepository"/>'s own doc comment for why this is a real implementation, not stubbed.</summary>
public sealed class MediaUploadSessionRepository(AppDbContext dbContext) : IMediaUploadSessionRepository
{
    /// <summary>Tracked — see <see cref="MediaAssetRepository.GetByIdAsync"/>'s own doc comment for why.</summary>
    public Task<MEDIA_UPLOAD_SESSION?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        dbContext.MediaUploadSessions().FirstOrDefaultAsync(s => s.MEDIA_UPLOAD_SESSION_ID == id, cancellationToken);

    public void Add(MEDIA_UPLOAD_SESSION uploadSession) => dbContext.MediaUploadSessions().Add(uploadSession);

    public Task SaveChangesAsync(CancellationToken cancellationToken) => dbContext.SaveChangesAsync(cancellationToken);
}
