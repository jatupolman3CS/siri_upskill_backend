using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Features.AddEpisodeAttachment;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.GetEpisodeAttachments;

public sealed class GetEpisodeAttachmentsHandler(
    AppDbContext dbContext,
    IEpisodeAccessReader episodeAccessReader)
{
    public async Task<Result<IReadOnlyList<EpisodeAttachmentResponse>>> HandleAsync(
        Guid episodeId,
        Guid? userId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var episode = await dbContext.CourseEpisodes()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == episodeId, cancellationToken)
            .ConfigureAwait(false);

        if (episode is null)
        {
            return Result.Failure<IReadOnlyList<EpisodeAttachmentResponse>>(DomainError.NotFound("ไม่พบบทเรียนที่ระบุ"));
        }

        var isAllowed = await EpisodeAccessHelper.CanUserAccessEpisodeAsync(
            dbContext,
            episodeAccessReader,
            episode,
            userId,
            isAdmin,
            cancellationToken).ConfigureAwait(false);

        if (!isAllowed)
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
                a.ContentType,
                a.SizeBytes,
                a.CreatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<EpisodeAttachmentResponse>>(attachments);
    }
}
