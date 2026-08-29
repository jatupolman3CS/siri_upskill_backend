using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
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
    long SizeBytes,
    byte[]? HeaderBytes = null);

public sealed record EpisodeAttachmentResponse(
    Guid Id,
    Guid EpisodeId,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTime CreatedAtUtc);

public sealed class AddEpisodeAttachmentValidator : AbstractValidator<AddEpisodeAttachmentCommand>
{
    public AddEpisodeAttachmentValidator(IOptions<EpisodeAttachmentOptions>? options = null)
    {
        var maxBytes = options?.Value.MaxFileSizeBytes ?? 52_428_800;

        RuleFor(c => c.FileName)
            .NotEmpty()
            .MaximumLength(255)
            .Must(AttachmentFileValidator.IsAllowedExtension)
            .WithMessage("นามสกุลไฟล์ไม่อยู่ในรายการที่อนุญาต หรือเป็นชนิดไฟล์ที่ไม่อนุญาตเพื่อความปลอดภัย");

        RuleFor(c => c.StorageKey)
            .NotEmpty()
            .MaximumLength(500);

        RuleFor(c => c.ContentType)
            .NotEmpty()
            .MaximumLength(100);

        RuleFor(c => c.SizeBytes)
            .GreaterThan(0)
            .LessThanOrEqualTo(maxBytes)
            .WithMessage($"ขนาดไฟล์ต้องไม่เกิน {maxBytes / (1024 * 1024)} MB");

        RuleFor(c => c)
            .Must(c =>
            {
                if (string.IsNullOrWhiteSpace(c.FileName) || string.IsNullOrWhiteSpace(c.ContentType) || c.SizeBytes <= 0)
                {
                    return true; // Field-level rules handle empty/zero
                }

                var res = AttachmentFileValidator.Validate(
                    c.FileName, c.ContentType, c.SizeBytes, c.HeaderBytes ?? [], maxBytes);
                return res.IsSuccess;
            })
            .WithMessage(c =>
            {
                var res = AttachmentFileValidator.Validate(
                    c.FileName ?? string.Empty, c.ContentType ?? string.Empty, c.SizeBytes, c.HeaderBytes ?? [], maxBytes);
                return res.Error.Message;
            });
    }
}

public sealed class AddEpisodeAttachmentHandler(
    AppDbContext dbContext,
    ICatalogPriceContract ownershipVerifier,
    IOptions<EpisodeAttachmentOptions> options,
    IAttachmentVirusScanner virusScanner)
{
    public async Task<Result<EpisodeAttachmentResponse>> HandleAsync(
        Guid episodeId,
        AddEpisodeAttachmentCommand command,
        Guid? currentUserId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        var maxBytes = options.Value.MaxFileSizeBytes;
        var validationResult = AttachmentFileValidator.Validate(
            command.FileName, command.ContentType, command.SizeBytes, command.HeaderBytes ?? [], maxBytes);

        if (validationResult.IsFailure)
        {
            return Result.Failure<EpisodeAttachmentResponse>(validationResult.Error);
        }

        var scanResult = await virusScanner.ScanBytesAsync(
            command.FileName, command.HeaderBytes ?? [], cancellationToken).ConfigureAwait(false);

        if (scanResult.IsFailure)
        {
            return Result.Failure<EpisodeAttachmentResponse>(scanResult.Error);
        }

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

        var attachment = EPISODE_ATTACHMENT.Create(
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
            attachment.ContentType,
            attachment.SizeBytes,
            attachment.CreatedAtUtc));
    }
}
