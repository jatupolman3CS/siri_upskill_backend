using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.DownloadLiveSessionAttachment;

/// <summary><paramref name="DownloadUrl"/> is a time-limited signed Cloudflare R2 URL, valid until
/// <paramref name="ExpiresAtUtc"/> — see <c>EpisodeAttachmentDownloadResponse</c>.</summary>
public sealed record LiveSessionAttachmentDownloadResponse(
    Guid Id,
    Guid SessionId,
    string FileName,
    string ContentType,
    long SizeBytes,
    string DownloadUrl,
    DateTime ExpiresAtUtc);

public sealed class DownloadLiveSessionAttachmentHandler(
    AppDbContext dbContext,
    ICatalogPriceContract ownershipVerifier,
    ILearningEnrollmentChecker enrollmentChecker,
    TeachingMaterialStorage materialStorage)
{
    public async Task<Result<LiveSessionAttachmentDownloadResponse>> HandleAsync(
        Guid sessionId,
        Guid attachmentId,
        Guid? userId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var session = await LiveSessionAttachmentAccess.FindSessionAsync(dbContext, sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return Result.Failure<LiveSessionAttachmentDownloadResponse>(DomainError.NotFound("ไม่พบคาบสอนที่ระบุ"));
        }

        var allowed = await LiveSessionAttachmentAccess.CanReadAsync(
            ownershipVerifier, enrollmentChecker, session.CourseId, userId, isAdmin, cancellationToken).ConfigureAwait(false);
        if (!allowed)
        {
            return Result.Failure<LiveSessionAttachmentDownloadResponse>(DomainError.NotFound("ไม่พบคาบสอนที่ระบุ"));
        }

        var attachment = await dbContext.LiveSessionAttachments()
            .AsNoTracking()
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.SessionId == sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (attachment is null)
        {
            return Result.Failure<LiveSessionAttachmentDownloadResponse>(DomainError.NotFound("ไม่พบไฟล์แนบที่ระบุ"));
        }

        var link = await materialStorage.CreateDownloadLinkAsync(attachment.StorageKey, cancellationToken).ConfigureAwait(false);
        if (link.IsFailure)
        {
            return Result.Failure<LiveSessionAttachmentDownloadResponse>(link.Error);
        }

        return Result.Success(new LiveSessionAttachmentDownloadResponse(
            attachment.Id,
            attachment.SessionId,
            attachment.FileName,
            attachment.ContentType,
            attachment.SizeBytes,
            link.Value.Url,
            link.Value.ExpiresAtUtc));
    }
}
