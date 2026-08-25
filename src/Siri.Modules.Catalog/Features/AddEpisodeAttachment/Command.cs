using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.AddEpisodeAttachment;

public sealed record AddEpisodeAttachmentCommand(
    string FileName,
    string StorageKey,
    string ContentType,
    long SizeBytes);

public sealed record EpisodeAttachmentResponse(
    Guid Id,
    Guid EpisodeId,
    string FileName,
    string StorageKey,
    string ContentType,
    long SizeBytes,
    DateTime CreatedAtUtc);

public sealed class AddEpisodeAttachmentValidator : AbstractValidator<AddEpisodeAttachmentCommand>
{
    public AddEpisodeAttachmentValidator()
    {
        RuleFor(c => c.FileName).NotEmpty().MaximumLength(255);
        RuleFor(c => c.StorageKey).NotEmpty().MaximumLength(500);
        RuleFor(c => c.ContentType).NotEmpty().MaximumLength(100);
        RuleFor(c => c.SizeBytes).GreaterThan(0);
    }
}

public sealed class AddEpisodeAttachmentHandler(
    AppDbContext dbContext,
    ICatalogPriceContract ownershipVerifier)
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

        var attachment = EpisodeAttachment.Create(
            episodeId,
            command.FileName,
            command.StorageKey,
            command.ContentType,
            command.SizeBytes);

        dbContext.EpisodeAttachments().Add(attachment);
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        return Result.Success(new EpisodeAttachmentResponse(
            attachment.Id,
            attachment.EpisodeId,
            attachment.FileName,
            attachment.StorageKey,
            attachment.ContentType,
            attachment.SizeBytes,
            attachment.CreatedAtUtc));
    }
}
