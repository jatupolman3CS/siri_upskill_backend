using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.DeleteLiveSessionAttachment;

public sealed class DeleteLiveSessionAttachmentHandler(
    AppDbContext dbContext,
    ICatalogPriceContract ownershipVerifier,
    TeachingMaterialStorage materialStorage)
{
    public async Task<Result> HandleAsync(
        Guid sessionId,
        Guid attachmentId,
        Guid? currentUserId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var session = await LiveSessionAttachmentAccess.FindSessionAsync(dbContext, sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return Result.Failure(DomainError.NotFound("ไม่พบคาบสอนที่ระบุ"));
        }

        if (!isAdmin)
        {
            if (currentUserId is null)
            {
                return Result.Failure(DomainError.Forbidden("จำเป็นต้องเข้าสู่ระบบ"));
            }

            var isOwner = await ownershipVerifier.IsInstructorOwnerOfCourseAsync(session.CourseId, currentUserId.Value, cancellationToken).ConfigureAwait(false);
            if (!isOwner)
            {
                return Result.Failure(DomainError.Forbidden("คุณไม่มีสิทธิ์ลบไฟล์แนบของคาบสอนนี้"));
            }
        }

        var attachment = await dbContext.LiveSessionAttachments()
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.SessionId == sessionId, cancellationToken)
            .ConfigureAwait(false);

        if (attachment is null)
        {
            return Result.Failure(DomainError.NotFound("ไม่พบไฟล์แนบที่ระบุ"));
        }

        var storageKey = attachment.StorageKey;

        dbContext.LiveSessionAttachments().Remove(attachment);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        // Row first, object second — see DeleteEpisodeAttachmentHandler.
        await materialStorage.DeleteQuietlyAsync([storageKey], cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
