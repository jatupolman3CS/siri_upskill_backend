using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.CreateLiveSession;

public sealed class CreateLiveSessionHandler(
    AppDbContext dbContext,
    IClock clock,
    ILiveMeetingSink liveMeetingSink,
    IOutputCacheStore outputCacheStore)
{
    private static readonly DomainError NotFoundError = DomainError.NotFound("ไม่พบคอร์สนี้");
    private static readonly DomainError NotOwnerError = DomainError.Forbidden("คุณไม่มีสิทธิ์แก้ไขคอร์สนี้");

    public async Task<Result<LiveSessionResponse>> HandleAsync(
        Guid userId,
        Guid courseId,
        CreateLiveSessionCommand command,
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

        if (course.DeliveryFormat == DeliveryFormat.OnDemand)
        {
            return Result.Failure<LiveSessionResponse>(
                DomainError.Conflict("เปลี่ยนรูปแบบคอร์สเป็น Live หรือ Hybrid ก่อนเพิ่มคาบสอนสด"));
        }

        if (course.Status == CourseStatus.Archived)
        {
            return Result.Failure<LiveSessionResponse>(
                DomainError.Conflict($"Cannot add a live session to a course in {course.Status} status."));
        }

        if (command.StartsAtUtc.Kind != DateTimeKind.Utc || command.EndsAtUtc.Kind != DateTimeKind.Utc)
        {
            return Result.Failure<LiveSessionResponse>(
                DomainError.Validation("startsAtUtc and endsAtUtc must be UTC (ISO-8601 with Z suffix)."));
        }

        if (command.StartsAtUtc <= clock.UtcNow)
        {
            return Result.Failure<LiveSessionResponse>(
                DomainError.Validation("A new live session must start in the future."));
        }

        var duration = command.EndsAtUtc - command.StartsAtUtc;
        if (duration < TimeSpan.FromMinutes(15) || duration > TimeSpan.FromHours(8))
        {
            return Result.Failure<LiveSessionResponse>(
                DomainError.Validation("Live session duration must be between 15 minutes and 8 hours."));
        }

        var overlaps = course.LiveSessions.Any(s =>
            s.Status == CourseLiveSessionStatus.Scheduled
            && s.StartsAtUtc < command.EndsAtUtc
            && command.StartsAtUtc < s.EndsAtUtc);

        if (overlaps)
        {
            return Result.Failure<LiveSessionResponse>(
                DomainError.Conflict("This time overlaps with another scheduled live session in this course."));
        }

        COURSE_LIVE_SESSION session;
        try
        {
            session = course.AddLiveSession(command.Title, command.Description, command.StartsAtUtc, command.EndsAtUtc, clock);
        }
        catch (ArgumentOutOfRangeException ex)
        {
            return Result.Failure<LiveSessionResponse>(DomainError.Validation(ex.Message));
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<LiveSessionResponse>(DomainError.Validation(ex.Message));
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<LiveSessionResponse>(DomainError.Conflict(ex.Message));
        }

        await liveMeetingSink.OnSessionScheduledAsync(session.Id, cancellationToken).ConfigureAwait(false);

        dbContext.Entry(session).State = EntityState.Added;
        await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);

        if (course.Status == CourseStatus.Published)
        {
            await outputCacheStore.EvictByTagAsync(CourseOutputCache.Tag, cancellationToken).ConfigureAwait(false);
        }

        return Result.Success(ToResponse(session));
    }

    public static LiveSessionResponse ToResponse(COURSE_LIVE_SESSION session) =>
        new(session.Id, session.CourseId, session.Title, session.Description,
            session.StartsAtUtc, session.EndsAtUtc, session.SortOrder,
            session.Status, session.CancelReason, session.RecordingEpisodeId);
}
