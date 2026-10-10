using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.GetLiveSessionAttachments;

public sealed record LiveSessionAttachmentResponse(
    Guid Id,
    Guid SessionId,
    string FileName,
    string ContentType,
    long SizeBytes,
    DateTime CreatedAtUtc);

public sealed class GetLiveSessionAttachmentsHandler(
    AppDbContext dbContext,
    ICatalogPriceContract ownershipVerifier,
    ILearningEnrollmentChecker enrollmentChecker)
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบคาบสอนที่ระบุ");

    public async Task<Result<IReadOnlyList<LiveSessionAttachmentResponse>>> HandleAsync(
        Guid sessionId,
        Guid? userId,
        bool isAdmin,
        CancellationToken cancellationToken)
    {
        var session = await LiveSessionAttachmentAccess.FindSessionAsync(dbContext, sessionId, cancellationToken).ConfigureAwait(false);
        if (session is null)
        {
            return Result.Failure<IReadOnlyList<LiveSessionAttachmentResponse>>(NotFoundError);
        }

        // Same answer as "no such session" for a caller without access, so ids can't be probed for existence.
        var allowed = await LiveSessionAttachmentAccess.CanReadAsync(
            ownershipVerifier, enrollmentChecker, session.CourseId, userId, isAdmin, cancellationToken).ConfigureAwait(false);
        if (!allowed)
        {
            return Result.Failure<IReadOnlyList<LiveSessionAttachmentResponse>>(NotFoundError);
        }

        var attachments = await dbContext.LiveSessionAttachments()
            .AsNoTracking()
            .Where(a => a.SessionId == sessionId)
            .OrderBy(a => a.CreatedAtUtc)
            .Select(a => new LiveSessionAttachmentResponse(
                a.Id,
                a.SessionId,
                a.FileName,
                a.ContentType,
                a.SizeBytes,
                a.CreatedAtUtc))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return Result.Success<IReadOnlyList<LiveSessionAttachmentResponse>>(attachments);
    }
}
