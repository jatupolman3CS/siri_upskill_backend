using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.AttachSessionRecording;
using Siri.Persistence;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Infrastructure.Contracts;

/// <summary>
/// <see cref="ILiveRecordingAttacher"/> (P11-13 contract section 5): Catalog's side of the automatic recording import.
/// <para>
/// Reading is one query per call that joins session -> course -> instructor profile (no per-row lookups) and projects straight into
/// <see cref="EndedLiveSession"/>, so a caller in another module never sees Catalog's domain types. The global soft-delete filter hides deleted courses; an
/// <see cref="CourseStatus.Archived"/> course is hidden too (nothing can be attached to it, and importing a multi-GB file only to be refused would be waste).
/// </para>
/// <para>
/// Attaching goes through <see cref="AttachSessionRecordingHandler"/> unchanged, so every rule of the instructor's manual attach holds for the import as
/// well: the instructor owns the course, the session has started, the asset is the instructor's own and <c>Ready</c>, one asset is one lesson, an archived
/// course is read-only. This class adds no rule of its own and relaxes none.
/// </para>
/// </summary>
public sealed class LiveRecordingAttacher(AppDbContext dbContext, AttachSessionRecordingHandler attachHandler) : ILiveRecordingAttacher
{
    /// <summary>Hard cap per call so a runaway caller cannot ask for the whole table.</summary>
    internal const int MaxListLimit = 500;

    public Task<IReadOnlyList<EndedLiveSession>> ListEndedAsync(
        DateTime endedAfterUtc, DateTime endedBeforeUtc, int limit, CancellationToken cancellationToken) =>
        ListEndedCoreAsync(instructorUserIds: null, endedAfterUtc, endedBeforeUtc, limit, cancellationToken);

    public Task<IReadOnlyList<EndedLiveSession>> ListEndedByInstructorsAsync(
        IReadOnlyCollection<Guid> instructorUserIds, DateTime endedAfterUtc, DateTime endedBeforeUtc, int limit, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(instructorUserIds);

        // The filter is inside the query (not applied to a page afterwards), so the limit counts only these instructors' sessions.
        return instructorUserIds.Count == 0
            ? Task.FromResult<IReadOnlyList<EndedLiveSession>>([])
            : ListEndedCoreAsync(instructorUserIds.Distinct().ToArray(), endedAfterUtc, endedBeforeUtc, limit, cancellationToken);
    }

    private async Task<IReadOnlyList<EndedLiveSession>> ListEndedCoreAsync(
        Guid[]? instructorUserIds, DateTime endedAfterUtc, DateTime endedBeforeUtc, int limit, CancellationToken cancellationToken)
    {
        if (limit <= 0 || endedBeforeUtc <= endedAfterUtc)
        {
            return [];
        }

        var rows =
            from s in dbContext.CourseLiveSessions().AsNoTracking()
            join c in dbContext.Courses().AsNoTracking() on s.CourseId equals c.Id
            join p in dbContext.InstructorProfiles().AsNoTracking() on c.InstructorId equals p.Id
            where c.Status != CourseStatus.Archived
            select new { s, p.UserId };

        if (instructorUserIds is not null)
        {
            rows = rows.Where(x => instructorUserIds.Contains(x.UserId));
        }

        return await rows
            .Where(x => x.s.EndsAtUtc >= endedAfterUtc && x.s.EndsAtUtc < endedBeforeUtc)
            .OrderBy(x => x.s.EndsAtUtc).ThenBy(x => x.s.Id)
            .Take(Math.Min(limit, MaxListLimit))
            .Select(x => new EndedLiveSession(
                x.s.Id,
                x.s.CourseId,
                x.UserId,
                x.s.Title,
                x.s.StartsAtUtc,
                x.s.EndsAtUtc,
                x.s.RecordingEpisodeId != null,
                x.s.Status == CourseLiveSessionStatus.Cancelled))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<EndedLiveSession?> GetAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        if (sessionId == Guid.Empty)
        {
            return null;
        }

        var rows =
            from s in dbContext.CourseLiveSessions().AsNoTracking()
            join c in dbContext.Courses().AsNoTracking() on s.CourseId equals c.Id
            join p in dbContext.InstructorProfiles().AsNoTracking() on c.InstructorId equals p.Id
            where c.Status != CourseStatus.Archived && s.Id == sessionId
            select new { s, p.UserId };

        return await rows
            .Select(x => new EndedLiveSession(
                x.s.Id,
                x.s.CourseId,
                x.UserId,
                x.s.Title,
                x.s.StartsAtUtc,
                x.s.EndsAtUtc,
                x.s.RecordingEpisodeId != null,
                x.s.Status == CourseLiveSessionStatus.Cancelled))
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Result<AttachedLiveRecording>> AttachAsync(
        Guid instructorUserId,
        Guid courseId,
        Guid sessionId,
        Guid mediaAssetId,
        string? episodeTitle,
        CancellationToken cancellationToken)
    {
        if (instructorUserId == Guid.Empty || courseId == Guid.Empty || sessionId == Guid.Empty || mediaAssetId == Guid.Empty)
        {
            return Result.Failure<AttachedLiveRecording>(DomainError.Validation("Instructor, course, session and media asset ids are required."));
        }

        var command = new AttachSessionRecordingCommand(mediaAssetId, EpisodeId: null, SectionId: null, CutTitle(episodeTitle));
        var attached = await attachHandler.HandleAsync(instructorUserId, courseId, sessionId, command, cancellationToken).ConfigureAwait(false);

        return attached.IsSuccess
            ? Result.Success(new AttachedLiveRecording(attached.Value.RecordingEpisodeId))
            : Result.Failure<AttachedLiveRecording>(attached.Error);
    }

    /// <summary>The lesson-title column length (the same limit the manual attach's validator enforces), never cutting a surrogate pair; blank becomes <c>null</c>
    /// so the handler falls back to its own default title.</summary>
    private static string? CutTitle(string? title)
    {
        if (string.IsNullOrWhiteSpace(title))
        {
            return null;
        }

        const int maxLength = AttachSessionRecordingCommandValidator.EpisodeTitleMaxLength;
        var trimmed = title.Trim();
        if (trimmed.Length <= maxLength)
        {
            return trimmed;
        }

        return trimmed[..(char.IsHighSurrogate(trimmed[maxLength - 1]) ? maxLength - 1 : maxLength)];
    }
}
