using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Features.AddEpisodeAttachment;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.GetEpisodeAttachments;

public sealed class GetEpisodeAttachmentsHandler(AppDbContext dbContext)
{
    public async Task<Result<IReadOnlyList<EpisodeAttachmentResponse>>> HandleAsync(
        Guid episodeId,
        CancellationToken cancellationToken)
    {
        var episodeExists = await dbContext.CourseEpisodes()
            .AsNoTracking()
            .AnyAsync(e => e.Id == episodeId, cancellationToken)
            .ConfigureAwait(false);

        if (!episodeExists)
        {
            return Result.Failure<IReadOnlyList<EpisodeAttachmentResponse>>(DomainError.NotFound("ไม่พบบทเรียนที่ระบุ"));
        }

        var attachments = await dbContext.EpisodeAttachments()
            .AsNoTracking()
            .Where(a => a.EpisodeId == episodeId)
            .OrderBy(a => a.CreatedAtUtc)
            .Select(a => new EpisodeAttachmentResponse(
                a.Id,
                a.EpisodeId,
                a.FileName,
                a.StorageKey,
                a.ContentType,
                a.SizeBytes,
                a.CreatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<EpisodeAttachmentResponse>>(attachments);
    }
}
