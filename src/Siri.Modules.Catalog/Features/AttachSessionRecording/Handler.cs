using Microsoft.AspNetCore.OutputCaching;
using Microsoft.EntityFrameworkCore;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Infrastructure;
using Siri.Persistence;
using Siri.SharedKernel;
using Siri.SharedKernel.Contracts;

namespace Siri.Modules.Catalog.Features.AttachSessionRecording;

/// <summary>
/// Attaches a teaching recording to a finished live session so it becomes <b>an ordinary lesson of the course</b> — that is the whole catch-up feature
/// (docs/contracts/P11-06-live-recording-catchup.md). There is no new entitlement logic anywhere: whoever may watch the course's lessons (including a learner
/// who enrolled <em>after</em> the class ended) may watch this one, because playback decides access by the lesson's course, never by the session.
/// <para>
/// <b>Why this does not reuse <c>CreateCourseEpisodeHandler</c>/<c>AttachEpisodeMediaHandler</c>:</b> those are limited to <c>Draft</c>/<c>Rejected</c> courses
/// (their <c>NotDraftError</c>), while the main use case here is a <em>Published</em> course that has just taught a class. Adding a lesson to a sold course
/// does not disturb any learner's progress (removing one would, and the domain still blocks that), so the handler calls the aggregate's own methods directly.
/// </para>
/// <para>
/// <b>Ownership</b> is the Catalog convention (<see cref="CreateLiveSession.CreateLiveSessionHandler"/>): unknown course = 404, someone else's course = 403 (a course
/// id is not a secret from instructors); an administrator is not an owner. The media asset must be the caller's own (<c>UploadedByUserId</c>), be <c>Ready</c> and
/// have a duration. Course, owner and asset checks all run before anything is mutated, and the whole change is one <c>SaveChangesAsync</c>.
/// </para>
/// <para>
/// The decision logic is <see cref="ApplyAsync"/> — internal and free of I/O apart from the media contract — so every branch is unit-tested against an in-memory
/// aggregate graph; this class only loads the graph, marks the rows the domain created as inserts, saves and evicts the public cache.
/// </para>
/// </summary>
public sealed class AttachSessionRecordingHandler(
    AppDbContext dbContext,
    IClock clock,
    IMediaAssetContract mediaAssets,
    IOutputCacheStore outputCacheStore)
{
    private static readonly DomainError CourseNotFoundError = DomainError.NotFound("ไม่พบคอร์สนี้");
    private static readonly DomainError NotOwnerError = DomainError.Forbidden("คุณไม่มีสิทธิ์แก้ไขคอร์สนี้");
    private static readonly DomainError ArchivedError = DomainError.Conflict("คอร์สที่เก็บถาวรแล้วแก้ไขไม่ได้");
    private static readonly DomainError SessionNotFoundError = DomainError.NotFound("ไม่พบคาบสอนสดนี้");
    private static readonly DomainError SectionNotFoundError = DomainError.NotFound("ไม่พบส่วนนี้");
    private static readonly DomainError EpisodeNotFoundError = DomainError.NotFound("ไม่พบบทเรียนนี้");
    private static readonly DomainError AssetNotFoundError = DomainError.NotFound("ไม่พบไฟล์วิดีโอที่ระบุ");
    private static readonly DomainError AssetNotOwnerError = DomainError.Forbidden("คุณไม่มีสิทธิ์ใช้งานไฟล์วิดีโอนี้");
    private static readonly DomainError ConcurrentEditError = DomainError.Conflict("ข้อมูลคอร์สนี้ถูกแก้ไขจากที่อื่นแล้ว กรุณาลองใหม่อีกครั้ง");

    private static readonly DomainError SessionNotStartedError =
        DomainError.Conflict("คาบสอนสดนี้ยังไม่เริ่ม จึงยังแนบบันทึกไม่ได้").WithReason(LiveRecordingReasons.SessionNotStarted);

    private static readonly DomainError AssetNotReadyError =
        DomainError.Conflict("วิดีโอยังประมวลผลไม่เสร็จ กรุณาลองใหม่อีกครั้ง").WithReason(LiveRecordingReasons.AssetNotReady);

    private static readonly DomainError AssetInUseError =
        DomainError.Conflict("วิดีโอนี้ถูกใช้เป็นบทเรียนอื่นในคอร์สนี้แล้ว").WithReason(LiveRecordingReasons.AssetInUse);

    private static readonly DomainError EpisodeHasNoMediaError =
        DomainError.Conflict("บทเรียนนี้ยังไม่มีวิดีโอ จึงใช้เป็นบันทึกการสอนไม่ได้").WithReason(LiveRecordingReasons.EpisodeHasNoMedia);

    private static readonly DomainError EpisodeInUseError =
        DomainError.Conflict("บทเรียนนี้เป็นบันทึกของคาบสอนสดอื่นอยู่แล้ว").WithReason(LiveRecordingReasons.EpisodeInUse);

    public async Task<Result<LiveSessionRecordingResponse>> HandleAsync(
        Guid userId,
        Guid courseId,
        Guid sessionId,
        AttachSessionRecordingCommand command,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        // The aggregate is loaded whole (sections, lessons, sessions): every rule below reads across them. Split query: three one-to-many levels would
        // otherwise multiply rows. The global soft-delete filter hides a deleted course, so it reads as absent.
        var course = await dbContext.Courses()
            .Include(c => c.Sections).ThenInclude(s => s.Episodes)
            .Include(c => c.LiveSessions)
            .AsSplitQuery()
            .FirstOrDefaultAsync(c => c.Id == courseId, cancellationToken)
            .ConfigureAwait(false);

        if (course is null)
        {
            return Result.Failure<LiveSessionRecordingResponse>(CourseNotFoundError);
        }

        var instructorProfileId = await dbContext.InstructorProfiles()
            .AsNoTracking()
            .Where(p => p.UserId == userId)
            .Select(p => (Guid?)p.Id)
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        var applied = await ApplyAsync(course, userId, instructorProfileId, sessionId, command, mediaAssets, clock, cancellationToken).ConfigureAwait(false);
        if (applied.IsFailure)
        {
            return Result.Failure<LiveSessionRecordingResponse>(applied.Error);
        }

        var outcome = applied.Value;
        MarkNewRowsAsInserts(dbContext, outcome);

        if (outcome.Changed)
        {
            try
            {
                await dbContext.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (DbUpdateConcurrencyException)
            {
                return Result.Failure<LiveSessionRecordingResponse>(ConcurrentEditError);
            }

            // The public detail page shows the lesson count / duration and the session's hasRecording flag.
            if (course.Status == CourseStatus.Published)
            {
                await outputCacheStore.EvictByTagAsync(CourseOutputCache.Tag, cancellationToken).ConfigureAwait(false);
            }
        }

        return Result.Success(outcome.Response);
    }

    /// <summary>
    /// The domain assigns every id up front (UUIDv7), so EF discovers the new children of this <em>tracked</em> aggregate as already-existing rows (Modified) and would
    /// UPDATE rows that are not there — mark them as inserts explicitly, exactly like the course-builder handlers do. Section first: the lesson's foreign key points at it.
    /// </summary>
    internal static void MarkNewRowsAsInserts(AppDbContext dbContext, AttachOutcome outcome)
    {
        if (outcome.NewSection is not null)
        {
            dbContext.Entry(outcome.NewSection).State = EntityState.Added;
        }

        if (outcome.NewEpisode is not null)
        {
            dbContext.Entry(outcome.NewEpisode).State = EntityState.Added;
        }
    }

    /// <summary>What <see cref="ApplyAsync"/> decided: the response, whether anything changed, and the rows the domain created.</summary>
    internal sealed record AttachOutcome(
        LiveSessionRecordingResponse Response,
        bool Changed,
        COURSE_SECTION? NewSection,
        COURSE_EPISODE? NewEpisode);

    /// <summary>
    /// The contract's ordered rules (section 3.1) applied to a loaded <paramref name="course"/> (sections, lessons and sessions included). Mutates the aggregate
    /// only after every check has passed; domain exceptions are mapped to <see cref="DomainError"/> (never a 500).
    /// </summary>
    /// <param name="callerInstructorProfileId">The caller's instructor profile id, or <c>null</c> when they have none.</param>
    internal static async Task<Result<AttachOutcome>> ApplyAsync(
        COURSE course,
        Guid userId,
        Guid? callerInstructorProfileId,
        Guid sessionId,
        AttachSessionRecordingCommand command,
        IMediaAssetContract mediaAssets,
        IClock clock,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(course);
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(mediaAssets);
        ArgumentNullException.ThrowIfNull(clock);

        if (callerInstructorProfileId is null || course.InstructorId != callerInstructorProfileId)
        {
            return Result.Failure<AttachOutcome>(NotOwnerError);
        }

        if (course.Status == CourseStatus.Archived)
        {
            return Result.Failure<AttachOutcome>(ArchivedError);
        }

        var session = course.LiveSessions.FirstOrDefault(s => s.Id == sessionId);
        if (session is null)
        {
            return Result.Failure<AttachOutcome>(SessionNotFoundError);
        }

        // A class that has not started has nothing to record. A Cancelled session is allowed (a class can be taught and then the rest cancelled).
        if (clock.UtcNow < session.StartsAtUtc)
        {
            return Result.Failure<AttachOutcome>(SessionNotStartedError);
        }

        try
        {
            return (command.MediaAssetId, command.EpisodeId) switch
            {
                ({ } assetId, null) => await AttachAssetAsync(course, session, userId, assetId, command, mediaAssets, cancellationToken).ConfigureAwait(false),
                (null, { } episodeId) => AttachExistingEpisode(course, session, episodeId),
                _ => Result.Failure<AttachOutcome>(DomainError.Validation("Provide exactly one of mediaAssetId or episodeId.")),
            };
        }
        catch (InvalidOperationException ex)
        {
            return Result.Failure<AttachOutcome>(DomainError.Conflict(ex.Message));
        }
        catch (ArgumentException ex)
        {
            return Result.Failure<AttachOutcome>(DomainError.Validation(ex.Message));
        }
    }

    /// <summary>Mode A: a <c>Ready</c> media asset of the caller becomes the session's recording lesson (new lesson, or replaced media on the existing one).</summary>
    private static async Task<Result<AttachOutcome>> AttachAssetAsync(
        COURSE course,
        COURSE_LIVE_SESSION session,
        Guid userId,
        Guid mediaAssetId,
        AttachSessionRecordingCommand command,
        IMediaAssetContract mediaAssets,
        CancellationToken cancellationToken)
    {
        var asset = await mediaAssets.GetAssetSummaryAsync(mediaAssetId, cancellationToken).ConfigureAwait(false);
        if (asset is null)
        {
            return Result.Failure<AttachOutcome>(AssetNotFoundError);
        }

        if (asset.UploadedByUserId != userId)
        {
            return Result.Failure<AttachOutcome>(AssetNotOwnerError);
        }

        if (!string.Equals(asset.Status, "Ready", StringComparison.Ordinal) || asset.DurationSeconds is not > 0)
        {
            return Result.Failure<AttachOutcome>(AssetNotReadyError);
        }

        var duration = asset.DurationSeconds.Value;
        var episodes = course.Sections.SelectMany(s => s.Episodes).ToList();

        // The lesson that already records this session — only if it is still on the course.
        var existing = session.RecordingEpisodeId is { } recordingId ? episodes.FirstOrDefault(e => e.Id == recordingId) : null;

        // One asset, one lesson: the only lesson allowed to hold it is the session's own recording lesson (replace / idempotent repeat).
        if (episodes.Any(e => e.MediaAssetId == asset.Id && e.Id != existing?.Id))
        {
            return Result.Failure<AttachOutcome>(AssetInUseError);
        }

        if (existing is not null)
        {
            if (existing.MediaAssetId == asset.Id)
            {
                // Same asset again: nothing to do, answer 200 as if freshly attached but report that nothing was replaced.
                return Result.Success(new AttachOutcome(ToResponse(session, existing, replacedExisting: false), Changed: false, NewSection: null, NewEpisode: null));
            }

            // Replace in place: the lesson, its position and everyone's progress on it stay; only the media changes.
            course.AttachEpisodeMedia(existing.Id, asset.Id, duration);
            if (CleanTitle(command.EpisodeTitle) is { } renamed)
            {
                existing.UpdateDetails(renamed, existing.Description);
            }

            return Result.Success(new AttachOutcome(ToResponse(session, existing, replacedExisting: true), Changed: true, NewSection: null, NewEpisode: null));
        }

        COURSE_SECTION? newSection = null;
        COURSE_SECTION section;
        if (command.SectionId is { } sectionId)
        {
            var requested = course.Sections.FirstOrDefault(s => s.Id == sectionId);
            if (requested is null)
            {
                return Result.Failure<AttachOutcome>(SectionNotFoundError);
            }

            section = requested;
        }
        else
        {
            // Reuse the recordings section if the course already has one (a second class must not create a second section).
            var reusable = course.Sections.OrderBy(s => s.SortOrder).FirstOrDefault(s => s.Title == LiveRecordingDefaults.SectionTitle);
            section = reusable ?? (newSection = course.AddSection(LiveRecordingDefaults.SectionTitle));
        }

        var title = CleanTitle(command.EpisodeTitle) ?? DefaultEpisodeTitle(session);
        var episode = course.AddEpisode(section.Id, title, description: null, isFreePreview: false);
        course.AttachEpisodeMedia(episode.Id, asset.Id, duration);
        course.AttachSessionRecording(session.Id, episode.Id);

        return Result.Success(new AttachOutcome(ToResponse(session, episode, replacedExisting: false), Changed: true, newSection, episode));
    }

    /// <summary>Mode B: link a lesson of this course that already has media as the session's recording.</summary>
    private static Result<AttachOutcome> AttachExistingEpisode(COURSE course, COURSE_LIVE_SESSION session, Guid episodeId)
    {
        var episode = course.Sections.SelectMany(s => s.Episodes).FirstOrDefault(e => e.Id == episodeId);
        if (episode is null)
        {
            return Result.Failure<AttachOutcome>(EpisodeNotFoundError);
        }

        if (episode.MediaAssetId is null)
        {
            return Result.Failure<AttachOutcome>(EpisodeHasNoMediaError);
        }

        // A lesson can be the recording of one class only.
        if (course.LiveSessions.Any(s => s.Id != session.Id && s.RecordingEpisodeId == episode.Id))
        {
            return Result.Failure<AttachOutcome>(EpisodeInUseError);
        }

        var previous = session.RecordingEpisodeId;
        if (previous == episode.Id)
        {
            return Result.Success(new AttachOutcome(ToResponse(session, episode, replacedExisting: false), Changed: false, NewSection: null, NewEpisode: null));
        }

        course.AttachSessionRecording(session.Id, episode.Id);

        return Result.Success(new AttachOutcome(ToResponse(session, episode, replacedExisting: previous is not null), Changed: true, NewSection: null, NewEpisode: null));
    }

    private static LiveSessionRecordingResponse ToResponse(COURSE_LIVE_SESSION session, COURSE_EPISODE episode, bool replacedExisting) =>
        new(
            session.Id,
            episode.Id,
            episode.SectionId,
            episode.Title,
            episode.DurationSeconds ?? 0,
            episode.MediaAssetId!.Value,
            replacedExisting);

    private static string? CleanTitle(string? title) => string.IsNullOrWhiteSpace(title) ? null : title.Trim();

    /// <summary>"บันทึก: {session title}" cut to the lesson-title column length (never in the middle of a surrogate pair).</summary>
    private static string DefaultEpisodeTitle(COURSE_LIVE_SESSION session)
    {
        const int maxLength = AttachSessionRecordingCommandValidator.EpisodeTitleMaxLength;

        var title = LiveRecordingDefaults.EpisodeTitlePrefix + session.Title.Trim();
        if (title.Length <= maxLength)
        {
            return title;
        }

        var cut = char.IsHighSurrogate(title[maxLength - 1]) ? maxLength - 1 : maxLength;
        return title[..cut];
    }
}
