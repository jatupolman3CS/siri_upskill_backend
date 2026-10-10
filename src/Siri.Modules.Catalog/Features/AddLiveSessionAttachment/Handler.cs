using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.GetLiveSessionAttachments;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.AddLiveSessionAttachment;

/// <summary>One uploaded file for a live session — see <c>AddEpisodeAttachmentCommand</c> for why the client sends only the bytes.</summary>
public sealed record AddLiveSessionAttachmentCommand(string FileName, string ContentType, Stream Content);

public sealed class AddLiveSessionAttachmentHandler(
    AppDbContext dbContext,
    ICatalogPriceContract ownershipVerifier,
    IOptions<EpisodeAttachmentOptions> options,
    TeachingMaterialStorage materialStorage,
    ILogger<AddLiveSessionAttachmentHandler> logger)
{
    public async Task<Result<LiveSessionAttachmentResponse>> HandleAsync(
        Guid sessionId,
        AddLiveSessionAttachmentCommand command,
        Guid? currentUserId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var session = await LiveSessionAttachmentAccess.FindSessionAsync(dbContext, sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return Result.Failure<LiveSessionAttachmentResponse>(DomainError.NotFound("ไม่พบคาบสอนที่ระบุ"));
        }

        // Ownership first: an unauthorized caller must not make us read, scan or store anything.
        if (!isAdmin)
        {
            if (currentUserId is null)
            {
                return Result.Failure<LiveSessionAttachmentResponse>(DomainError.Forbidden("จำเป็นต้องเข้าสู่ระบบ"));
            }

            var isOwner = await ownershipVerifier.IsInstructorOwnerOfCourseAsync(session.CourseId, currentUserId.Value, cancellationToken).ConfigureAwait(false);
            if (!isOwner)
            {
                return Result.Failure<LiveSessionAttachmentResponse>(DomainError.Forbidden("คุณไม่มีสิทธิ์แก้ไขคาบสอนนี้"));
            }
        }

        // Materials can be added after a session has ended (post-class slides) — only a cancelled one is closed.
        if (session.Status == CourseLiveSessionStatus.Cancelled)
        {
            return Result.Failure<LiveSessionAttachmentResponse>(DomainError.Conflict("คาบสอนนี้ถูกยกเลิกแล้ว ไม่สามารถเพิ่มไฟล์แนบได้"));
        }

        var existingCount = await dbContext.LiveSessionAttachments()
            .CountAsync(a => a.SessionId == sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (existingCount >= options.Value.MaxAttachmentsPerParent)
        {
            return Result.Failure<LiveSessionAttachmentResponse>(
                DomainError.Conflict($"คาบสอนนี้มีไฟล์แนบครบจำนวนสูงสุดแล้ว ({options.Value.MaxAttachmentsPerParent} ไฟล์)"));
        }

        var stored = await materialStorage.StoreAsync(
            TeachingMaterialStorage.LiveSessionKeyPrefix(session.CourseId, session.Id),
            new MaterialUpload(command.FileName, command.ContentType, command.Content),
            cancellationToken).ConfigureAwait(false);

        if (stored.IsFailure)
        {
            return Result.Failure<LiveSessionAttachmentResponse>(stored.Error);
        }

        var attachment = LIVE_SESSION_ATTACHMENT.Create(
            sessionId,
            stored.Value.FileName,
            stored.Value.StorageKey,
            stored.Value.ContentType,
            stored.Value.SizeBytes);

        try
        {
            dbContext.LiveSessionAttachments().Add(attachment);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            logger.LogError("Saving live-session attachment row failed after upload; removing object {StorageKey}.", stored.Value.StorageKey);
            await materialStorage.DeleteQuietlyAsync([stored.Value.StorageKey], CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        return Result.Success(new LiveSessionAttachmentResponse(
            attachment.Id,
            attachment.SessionId,
            attachment.FileName,
            attachment.ContentType,
            attachment.SizeBytes,
            attachment.CreatedAtUtc));
    }
}
