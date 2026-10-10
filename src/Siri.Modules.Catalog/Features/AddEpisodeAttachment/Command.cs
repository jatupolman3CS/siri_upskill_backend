using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Application;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.AddEpisodeAttachment;

/// <summary>
/// One uploaded file for an episode. Built by the controller from the multipart <c>file</c> part — the client
/// no longer says where the file is stored, how big it is or what its first bytes are; the server reads all of
/// that from <see cref="Content"/> itself (see <see cref="TeachingMaterialStorage"/>).
/// </summary>
public sealed record AddEpisodeAttachmentCommand(string FileName, string ContentType, Stream Content);

public sealed record EpisodeAttachmentResponse(
    Guid Id,
    Guid EpisodeId,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTime CreatedAtUtc);

public sealed class AddEpisodeAttachmentHandler(
    AppDbContext dbContext,
    ICatalogPriceContract ownershipVerifier,
    IOptions<EpisodeAttachmentOptions> options,
    TeachingMaterialStorage materialStorage,
    ILogger<AddEpisodeAttachmentHandler> logger)
{
    public async Task<Result<EpisodeAttachmentResponse>> HandleAsync(
        Guid episodeId,
        AddEpisodeAttachmentCommand command,
        Guid? currentUserId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var episode = await dbContext.CourseEpisodes()
            .AsNoTracking()
            .FirstOrDefaultAsync(e => e.Id == episodeId, cancellationToken)
            .ConfigureAwait(false);

        if (episode is null)
        {
            return Result.Failure<EpisodeAttachmentResponse>(DomainError.NotFound("ไม่พบบทเรียนที่ระบุ"));
        }

        // Ownership first: an unauthorized caller must not make us read, scan or store anything.
        if (!isAdmin)
        {
            if (currentUserId is null)
            {
                return Result.Failure<EpisodeAttachmentResponse>(DomainError.Forbidden("จำเป็นต้องเข้าสู่ระบบ"));
            }

            var isOwner = await ownershipVerifier.IsInstructorOwnerOfEpisodeAsync(episodeId, currentUserId.Value, cancellationToken).ConfigureAwait(false);
            if (!isOwner)
            {
                return Result.Failure<EpisodeAttachmentResponse>(DomainError.Forbidden("คุณไม่มีสิทธิ์แก้ไขบทเรียนนี้"));
            }
        }

        var existingCount = await dbContext.EpisodeAttachments()
            .CountAsync(a => a.EpisodeId == episodeId, cancellationToken)
            .ConfigureAwait(false);

        if (existingCount >= options.Value.MaxAttachmentsPerParent)
        {
            return Result.Failure<EpisodeAttachmentResponse>(
                DomainError.Conflict($"บทเรียนนี้มีไฟล์แนบครบจำนวนสูงสุดแล้ว ({options.Value.MaxAttachmentsPerParent} ไฟล์)"));
        }

        var stored = await materialStorage.StoreAsync(
            TeachingMaterialStorage.EpisodeKeyPrefix(episode.CourseId, episode.Id),
            new MaterialUpload(command.FileName, command.ContentType, command.Content),
            cancellationToken).ConfigureAwait(false);

        if (stored.IsFailure)
        {
            return Result.Failure<EpisodeAttachmentResponse>(stored.Error);
        }

        var attachment = EPISODE_ATTACHMENT.Create(
            episodeId,
            stored.Value.FileName,
            stored.Value.StorageKey,
            stored.Value.ContentType,
            stored.Value.SizeBytes);

        try
        {
            dbContext.EpisodeAttachments().Add(attachment);
            await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // The row never made it: don't leave the freshly uploaded object behind with nothing pointing at it.
            // CancellationToken.None on purpose — the request being cancelled is exactly when cleanup matters most.
            logger.LogError("Saving episode attachment row failed after upload; removing object {StorageKey}.", stored.Value.StorageKey);
            await materialStorage.DeleteQuietlyAsync([stored.Value.StorageKey], CancellationToken.None).ConfigureAwait(false);
            throw;
        }

        return Result.Success(new EpisodeAttachmentResponse(
            attachment.Id,
            attachment.EpisodeId,
            attachment.FileName,
            attachment.ContentType,
            attachment.SizeBytes,
            attachment.CreatedAtUtc));
    }
}
