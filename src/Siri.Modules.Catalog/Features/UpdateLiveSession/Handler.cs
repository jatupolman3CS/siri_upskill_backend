using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.CreateLiveSession;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.UpdateLiveSession;

public sealed class UpdateLiveSessionHandler(
    AppDbContext dbContext,
    IClock clock,
    ILiveMeetingSink liveMeetingSink,
    IOutputCacheStore outputCacheStore)
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบคอร์สหรือคาบสอนสดนี้");
    private static readonly DomainError NotOwnerError = DomainError.Forbidden("คุณไม่มีสิทธิ์แก้ไขคอร์สนี้");

    public async Task<Result<LiveSessionResponse>> HandleAsync(
        Guid userId,
        Guid courseId,
        Guid sessionId,
        UpdateLiveSessionCommand command,
        CancellationToken cancellationToken)
    {
        var course = await dbContext.Courses()
            .Include(c => c.LiveSessions)
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken)
            .ConfigureAwait(false);

        if (course is null)
        {
            return Result.Failure<LiveSessionResponse>(NotFoundError);
        }

        var instructorProfile = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .FirstOrDefaultAsync(p => p.UserId == userId, cancellationToken)
            .ConfigureAwait(false);

        if (instructorProfile is null || course.InstructorId != instructorProfile.Id)
        {
            return Result.Failure<LiveSessionResponse>(NotOwnerError);
        }

        var session = course.LiveSessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is null)
        {
            return Result.Failure<LiveSessionResponse>(NotFoundError);
        }

        if (session.Status != CourseLiveSessionStatus.Scheduled)
        {
            return Result.Failure<LiveSessionResponse>(
                DomainError.Conflict($"Cannot update a live session in {session.Status} status."));
        }

        if (session.EndsAtUtc <= clock.UtcNow)
        {
            return Result.Failure<LiveSessionResponse>(
                DomainError.Conflict("Cannot update a live session that has already ended."));
        }

        // Window rules (UTC, 15 min-8 h duration, no overlap, ends in the future) live only in
        // COURSE.UpdateLiveSession; their exceptions are mapped to Validation/Conflict below.
        try
        {
            course.UpdateLiveSession(sessionId, command.Title, command.Description, command.StartsAtUtc, command.EndsAtUtc, clock);
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<LiveSessionResponse>(DomainError.Validation(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<LiveSessionResponse>(DomainError.Conflict(ex.Message));
        }

        await liveMeetingSink.OnSessionChangedAsync(sessionId, cancellationToken).ConfigureAwait(false);

        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (course.Status == CourseStatus.Published)
        {
            await outputCacheStore.EvictByTagAsync(CourseOutputCache.Tag, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success(CreateLiveSessionHandler.ToResponse(session));
    }
}
