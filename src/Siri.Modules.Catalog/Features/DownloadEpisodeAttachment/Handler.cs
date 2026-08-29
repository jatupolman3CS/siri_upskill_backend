using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.DownloadEpisodeAttachment;

public sealed record EpisodeAttachmentDownloadResponse(
    Guid Id,
    Guid EpisodeId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string DownloadUrl);

public sealed class DownloadEpisodeAttachmentHandler(
    AppDbContext dbContext,
    IEpisodeAccessReader episodeAccessReader)
{
    public async Task<Result<EpisodeAttachmentDownloadResponse>> HandleAsync(
        Guid episodeId,
        Guid attachmentId,
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
            return Result.Failure<EpisodeAttachmentDownloadResponse>(DomainError.NotFound("ไม่พบบทเรียนที่ระบุ"));
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
            return Result.Failure<EpisodeAttachmentDownloadResponse>(DomainError.NotFound("ไม่พบบทเรียนที่ระบุ"));
        }

        var attachment = await dbContext.EpisodeAttachments()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.EpisodeId == episodeId, cancellationToken)
            .ConfigureAwait(false);

        if (attachment is null)
        {
            return Result.Failure<EpisodeAttachmentDownloadResponse>(DomainError.NotFound("ไม่พบไฟล์แนบที่ระบุ"));
        }

        var downloadUrl = $"/api/catalog/episodes/{episodeId}/attachments/{attachmentId}/download";

        return Result.Success(new EpisodeAttachmentDownloadResponse(
            attachment.Id,
            attachment.EpisodeId,
            attachment.FileName,
            attachment.ContentType,
            attachment.SizeBytes,
            downloadUrl));
    }
}
