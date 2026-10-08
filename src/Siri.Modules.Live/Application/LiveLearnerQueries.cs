using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Live.Contracts;
using Siri.Modules.Live.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Live.Application;

/// <summary>
/// The learner's read side of live teaching (docs/contracts/P11-05-live-learner-instructor-api-join-gate.md sections 4.2 and 4.3): the class list of a course
/// they are enrolled in, the next classes across all their courses, and a calendar file for one class. Every method takes the caller's user id from the
/// authenticated principal (the controller passes it in), and <b>none of them ever returns a room link</b> — that is the join gate's job alone.
/// <para>
/// Entitlement and the "not found" answer are the same as the join gate's (<see cref="LiveErrors.NotFound"/>): not enrolled, lapsed, or no such course/session
/// look identical. <c>displayState</c>/<c>canJoin</c> come from <see cref="LiveSessionDisplayStateCalculator"/> with the server clock and the configured join
/// window, so they always agree with what <see cref="SessionJoinService.JoinAsync"/> will decide a moment later.
/// </para>
/// </summary>
public sealed class LiveLearnerQueries(
    ILiveScheduleReader schedule,
    ILearningAccessContract learning,
    ICourseSummaryReader courses,
    ISessionMeetingRepository meetings,
    ISessionInviteReader invites,
    ILiveAttendanceReader attendance,
    SessionJoinService joinService,
    IClock clock,
    IOptions<LiveOptions> options)
{
    public const string Timezone = "Asia/Bangkok";

    public const int DefaultUpcomingLimit = 10;

    public const int MaxUpcomingLimit = 20;

    /// <summary>The organizer name written into downloadable calendar files.</summary>
    public const string CalendarOrganizerName = "SIRI UpSkill";

    /// <summary>The classes of one course for a learner who holds an active enrollment of it, soonest first, cancelled ones included.</summary>
    public async Task<Result<MySessionsResponse>> GetMySessionsAsync(Guid userId, Guid courseId, CancellationToken cancellationToken)
    {
        if (!await learning.HasActiveEnrollmentAsync(userId, courseId, cancellationToken).ConfigureAwait(false))
        {
            return Result.Failure<MySessionsResponse>(LiveErrors.NotFound);
        }

        var now = clock.UtcNow;
        var window = options.Value.JoinWindowBeforeMinutes;

        var contexts = (await schedule
                .GetSessionContextsForCoursesAsync([courseId], fromUtc: null, toUtc: null, includeCancelled: true, cancellationToken)
                .ConfigureAwait(false))
            .OrderBy(c => c.StartsAtUtc).ThenBy(c => c.SessionId)
            .ToList();

        // The slug is needed even when the course has no class yet; the summary reader also covers a course that is no longer published.
        string slug;
        if (contexts.Count > 0)
        {
            slug = contexts[0].CourseSlug;
        }
        else
        {
            var summaries = await courses.GetCourseSummariesAsync([courseId], cancellationToken).ConfigureAwait(false);
            if (!summaries.TryGetValue(courseId, out var summary))
            {
                return Result.Failure<MySessionsResponse>(LiveErrors.NotFound);
            }

            slug = summary.Slug;
        }

        var sessionIds = contexts.Select(c => c.SessionId).ToArray();

        // One batched read per fact (no per-session queries).
        var rooms = sessionIds.Length == 0
            ? new Dictionary<Guid, bool>()
            : (await meetings.GetBySessionIdsAsync(sessionIds, cancellationToken).ConfigureAwait(false)).ToDictionary(m => m.SESSION_ID, m => m.IsUsable);
        var inviteStatuses = sessionIds.Length == 0
            ? new Dictionary<Guid, InviteStatus>()
            : (await invites.GetStatusesForUserAsync(userId, sessionIds, cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.Key, p => p.Value);
        var attended = await attendance.GetCourseIdsAttendedAsync(userId, [courseId], cancellationToken).ConfigureAwait(false);

        var items = contexts.Select(c =>
        {
            var state = LiveSessionDisplayStateCalculator.Compute(c.Status, c.StartsAtUtc, c.EndsAtUtc, now, window);

            return new MySessionItem(
                c.SessionId,
                c.Title,
                c.Description,
                c.StartsAtUtc,
                c.EndsAtUtc,
                state,
                c.Status.ToString(),
                c.CancelReason,
                CanJoin: state == LiveSessionDisplayState.Live,
                JoinOpensAtUtc: c.StartsAtUtc.AddMinutes(-window),
                RoomReady: rooms.GetValueOrDefault(c.SessionId),
                HasRecording: c.RecordingEpisodeId is not null,
                c.RecordingEpisodeId,
                InviteStatus: inviteStatuses.TryGetValue(c.SessionId, out var inviteStatus) ? inviteStatus.ToString() : null);
        }).ToList();

        return new MySessionsResponse(courseId, slug, Timezone, now, attended.Contains(courseId), items);
    }

    /// <summary>The next classes (not finished, not cancelled) across every course the caller is actively enrolled in, soonest first, at most <paramref name="limit"/>
    /// (clamped to 1-20; a non-positive value means the default of 10).</summary>
    public async Task<MyUpcomingSessionsResponse> GetUpcomingAsync(Guid userId, int limit, CancellationToken cancellationToken)
    {
        var effectiveLimit = limit < 1 ? DefaultUpcomingLimit : Math.Min(limit, MaxUpcomingLimit);

        var now = clock.UtcNow;
        var window = options.Value.JoinWindowBeforeMinutes;

        var courseIds = await learning.GetActiveEnrolledCourseIdsAsync(userId, cancellationToken).ConfigureAwait(false);
        if (courseIds.Count == 0)
        {
            return new MyUpcomingSessionsResponse(now, []);
        }

        // Overlap window [now, infinity): a class that is running right now is included. The state is re-checked below so one that ended on this very
        // instant (or was cancelled) can never slip through.
        var contexts = await schedule
            .GetSessionContextsForCoursesAsync(courseIds, fromUtc: now, toUtc: null, includeCancelled: false, cancellationToken)
            .ConfigureAwait(false);

        var items = contexts
            .Select(c => (Context: c, State: LiveSessionDisplayStateCalculator.Compute(c.Status, c.StartsAtUtc, c.EndsAtUtc, now, window)))
            .Where(x => x.State is LiveSessionDisplayState.Upcoming or LiveSessionDisplayState.Live)
            .OrderBy(x => x.Context.StartsAtUtc).ThenBy(x => x.Context.SessionId)
            .Take(effectiveLimit)
            .Select(x => new MyUpcomingSessionItem(
                x.Context.SessionId,
                x.Context.CourseId,
                x.Context.CourseTitle,
                x.Context.CourseSlug,
                x.Context.Title,
                x.Context.StartsAtUtc,
                x.Context.EndsAtUtc,
                x.State,
                CanJoin: x.State == LiveSessionDisplayState.Live,
                JoinOpensAtUtc: x.Context.StartsAtUtc.AddMinutes(-window)))
            .ToList();

        return new MyUpcomingSessionsResponse(now, items);
    }

    /// <summary>
    /// A single-event <c>PUBLISH</c> calendar for one class, for "add to my calendar". The caller must be entitled exactly as for joining (an owner may download
    /// their own); a cancelled or finished class is a 409. UID and SEQUENCE match the invitation e-mails (<c>SESSION_MEETINGS.ICS_SEQUENCE</c>), and the event's
    /// location/URL is the platform's own join page — the room link is never in the file.
    /// </summary>
    public async Task<Result<LiveCalendarFile>> GetCalendarAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var resolved = await joinService.ResolveEntitledSessionAsync(userId, sessionId, cancellationToken).ConfigureAwait(false);
        if (resolved.IsFailure)
        {
            return Result.Failure<LiveCalendarFile>(resolved.Error);
        }

        var context = resolved.Value.Context;
        var now = clock.UtcNow;

        if (context.Status == LiveSessionStatus.Cancelled)
        {
            return Result.Failure<LiveCalendarFile>(LiveErrors.SessionCancelled());
        }

        if (context.EndsAtUtc <= now)
        {
            return Result.Failure<LiveCalendarFile>(LiveErrors.SessionEnded(context, withRecordingHint: false));
        }

        var live = options.Value;
        var publicBase = live.GetNormalizedPublicBaseUrl();
        var uidHost = Uri.TryCreate(publicBase, UriKind.Absolute, out var baseUri) ? baseUri.IdnHost : "localhost";

        var meeting = (await meetings.GetBySessionIdsAsync([sessionId], cancellationToken).ConfigureAwait(false)).FirstOrDefault();

        var calendarEvent = new IcsEvent(
            sessionId,
            Sequence: meeting?.ICS_SEQUENCE ?? 0,
            Summary: $"{context.Title} — {context.CourseTitle}",
            context.Description,
            context.StartsAtUtc,
            context.EndsAtUtc,
            JoinUrl: $"{publicBase}/live/{sessionId}/join",
            Cancelled: false);

        var content = IcsCalendarBuilder.Build(
            IcsMethod.Publish,
            [calendarEvent],
            live.OrganizerEmail,
            CalendarOrganizerName,
            uidHost,
            attendeeEmail: null,
            attendeeName: null,
            now,
            live.JoinWindowBeforeMinutes);

        return new LiveCalendarFile($"live-{sessionId:N}.ics", content);
    }
}
