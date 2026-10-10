using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.DownloadEpisodeAttachment;

/// <summary>
/// <paramref name="DownloadUrl"/> is a time-limited, signed Cloudflare R2 URL (expires at
/// <paramref name="ExpiresAtUtc"/>) minted right after the entitlement check — hand it straight to the browser, don't store it.
/// </summary>
public sealed record EpisodeAttachmentDownloadResponse(
    Guid Id,
    Guid EpisodeId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string DownloadUrl,
    DateTime ExpiresAtUtc);

public sealed class DownloadEpisodeAttachmentHandler(
    AppDbContext dbContext,
    IEpisodeAccessReader episodeAccessReader,
    TeachingMaterialStorage materialStorage)
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

        var link = await materialStorage.CreateDownloadLinkAsync(attachment.StorageKey, cancellationToken).ConfigureAwait(false);
        if (link.IsFailure)
        {
            return Result.Failure<EpisodeAttachmentDownloadResponse>(link.Error);
        }

        return Result.Success(new EpisodeAttachmentDownloadResponse(
            attachment.Id,
            attachment.EpisodeId,
            attachment.FileName,
            attachment.ContentType,
            attachment.SizeBytes,
            link.Value.Url,
            link.Value.ExpiresAtUtc));
    }
}
