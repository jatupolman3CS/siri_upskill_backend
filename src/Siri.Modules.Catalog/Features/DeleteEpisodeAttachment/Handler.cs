using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.DeleteEpisodeAttachment;

public sealed class DeleteEpisodeAttachmentHandler(
    AppDbContext dbContext,
    ICatalogPriceContract ownershipVerifier)
{
    public async Task<Result> HandleAsync(
        Guid episodeId,
        Guid attachmentId,
        Guid? currentUserId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var attachment = await dbContext.EpisodeAttachments()
            .FirstOrDefaultAsync(a => a.Id == attachmentId && a.EpisodeId == episodeId, cancellationToken)
            .ConfigureAwait(false);

        if (attachment is null)
        {
            return Result.Failure(DomainError.NotFound("ไม่พบไฟล์แนบที่ระบุ"));
        }

        if (!isAdmin)
        {
            if (currentUserId is null)
            {
                return Result.Failure(DomainError.Forbidden("จำเป็นต้องเข้าสู่ระบบ"));
            }

            var isOwner = await ownershipVerifier.IsInstructorOwnerOfEpisodeAsync(episodeId, currentUserId.Value, cancellationToken).ConfigureAwait(false);
            if (!isOwner)
            {
                return Result.Failure(DomainError.Forbidden("คุณไม่มีสิทธิ์ลบไฟล์แนบของบทเรียนนี้"));
            }
        }

        dbContext.EpisodeAttachments().Remove(attachment);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success();
    }
}
