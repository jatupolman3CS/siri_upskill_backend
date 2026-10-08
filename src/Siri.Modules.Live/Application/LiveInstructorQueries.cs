using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Live.Contracts;
using Siri.Modules.Live.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Live.Application;

/// <summary>
/// The instructor's read side of live teaching (docs/contracts/P11-05-live-learner-instructor-api-join-gate.md section 4.4): their session list, one session's
/// detail (the only place besides the join gate that reveals the room link) and a roster of who is coming / has joined.
/// <para>
/// <b>Ownership:</b> the caller's user id comes from the authenticated principal; a session is theirs only if <c>LiveSessionContext.InstructorUserId</c> equals it —
/// an administrator is <em>not</em> an owner. Unlike the learner side, an instructor may tell "not yours" (403) from "does not exist" (404): their own courses and
/// sessions are not secrets from them (same convention as Catalog's handlers). The list simply contains only the caller's sessions.
/// </para>
/// <para>
/// <b>Privacy:</b> a roster shows a display name and a <em>masked</em> e-mail (<see cref="EmailMasker"/>), never the full address; contacts are read once, in a batch,
/// and the room link appears only in <see cref="GetSessionDetailAsync"/>.
/// </para>
/// </summary>
public sealed class LiveInstructorQueries(
    ILiveScheduleReader schedule,
    ILearningAccessContract learning,
    ISessionMeetingRepository meetings,
    SessionMeetingService meetingService,
    ISessionJoinLogRepository joinLogs,
    ISessionInviteReader invites,
    ILiveAttendanceReader attendance,
    IUserContactReader contacts,
    IClock clock,
    IOptions<LiveOptions> options)
{
    public const int DefaultSessionPageSize = 20;

    public const int MaxSessionPageSize = 50;

    public const int DefaultRosterPageSize = 50;

    public const int MaxRosterPageSize = 100;

    /// <summary>The instructor's sessions across all their courses (or one course), paged. A <paramref name="courseId"/> that is not one of the caller's courses
    /// simply matches nothing (an empty page), never another instructor's data.</summary>
    public async Task<PagedResult<InstructorSessionListItem>> GetSessionsAsync(
        Guid userId,
        InstructorSessionScope scope,
        Guid? courseId,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var effectivePage = page < 1 ? 1 : page;
        var effectivePageSize = pageSize < 1 ? DefaultSessionPageSize : Math.Min(pageSize, MaxSessionPageSize);
        var skip = (effectivePage - 1) * effectivePageSize;

        var now = clock.UtcNow;
        var window = options.Value.JoinWindowBeforeMinutes;

        // Upcoming/past use the reader's overlap semantics: a class in progress is both "not over" and "already started".
        var (fromUtc, toUtc, includeCancelled, newestFirst) = scope switch
        {
            InstructorSessionScope.Upcoming => ((DateTime?)now, (DateTime?)null, false, false),
            InstructorSessionScope.Past => ((DateTime?)null, (DateTime?)now, true, true),
            _ => ((DateTime?)null, (DateTime?)null, true, true),
        };

        IReadOnlyList<LiveSessionContext> pageItems;
        int totalCount;

        if (courseId is { } onlyCourse)
        {
            // The reader's per-instructor page has no course filter, so the course's (small) session set is read and paged here — and filtered to the
            // caller's own sessions, which is also what makes someone else's course id harmless.
            var all = (await schedule
                    .GetSessionContextsForCoursesAsync([onlyCourse], fromUtc, toUtc, includeCancelled, cancellationToken)
                    .ConfigureAwait(false))
                .Where(c => c.InstructorUserId == userId);

            var ordered = (newestFirst
                    ? all.OrderByDescending(c => c.StartsAtUtc).ThenByDescending(c => c.SessionId)
                    : all.OrderBy(c => c.StartsAtUtc).ThenBy(c => c.SessionId))
                .ToList();

            totalCount = ordered.Count;
            pageItems = ordered.Skip(skip).Take(effectivePageSize).ToList();
        }
        else
        {
            var result = await schedule
                .GetInstructorSessionContextsAsync(userId, fromUtc, toUtc, includeCancelled, newestFirst, skip, effectivePageSize, cancellationToken)
                .ConfigureAwait(false);

            totalCount = result.TotalCount;
            pageItems = result.Items;
        }

        if (pageItems.Count == 0)
        {
            return PagedResult<InstructorSessionListItem>.Create([], totalCount, effectivePage, effectivePageSize);
        }

        var sessionIds = pageItems.Select(c => c.SessionId).ToArray();
        var rooms = (await meetings.GetBySessionIdsAsync(sessionIds, cancellationToken).ConfigureAwait(false)).ToDictionary(m => m.SESSION_ID);
        var stats = await attendance.GetSessionStatsAsync(sessionIds, cancellationToken).ConfigureAwait(false);

        var items = pageItems.Select(c =>
        {
            stats.TryGetValue(c.SessionId, out var sessionStats);

            return new InstructorSessionListItem(
                c.SessionId,
                c.CourseId,
                c.CourseTitle,
                c.Title,
                c.StartsAtUtc,
                c.EndsAtUtc,
                LiveSessionDisplayStateCalculator.Compute(c.Status, c.StartsAtUtc, c.EndsAtUtc, now, window),
                sessionStats?.ExpectedLearners ?? 0,
                sessionStats?.JoinedLearners ?? 0,
                MeetingSummaryOf(c.SessionId, rooms),
                c.RecordingEpisodeId,
                c.CancelReason);
        }).ToList();

        return PagedResult<InstructorSessionListItem>.Create(items, totalCount, effectivePage, effectivePageSize);
    }

    /// <summary>One session of the caller's, including the room link (<c>null</c> while there is no usable room). 404 when it does not exist, 403 when it is someone else's.</summary>
    public async Task<Result<InstructorSessionDetailResponse>> GetSessionDetailAsync(Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var owned = await GetOwnedSessionAsync<InstructorSessionDetailResponse>(userId, sessionId, cancellationToken).ConfigureAwait(false);
        if (owned.Failure is { } failure)
        {
            return failure;
        }

        var context = owned.Context!;
        var now = clock.UtcNow;

        var meeting = (await meetings.GetBySessionIdsAsync([sessionId], cancellationToken).ConfigureAwait(false)).FirstOrDefault();
        var stats = (await attendance.GetSessionStatsAsync([sessionId], cancellationToken).ConfigureAwait(false)).GetValueOrDefault(sessionId);
        var enrolled = await learning.GetActiveEnrolledUserIdsAsync(context.CourseId, cancellationToken).ConfigureAwait(false);

        // A room that was deleted (cancelled session) has nothing worth showing even if its old ciphertext is still stored.
        var meetUrl = meeting is { IsUsable: true } ? meetingService.RevealUrl(meeting) : null;

        return new InstructorSessionDetailResponse(
            context.SessionId,
            context.CourseId,
            context.CourseTitle,
            context.CourseSlug,
            context.Title,
            context.Description,
            context.StartsAtUtc,
            context.EndsAtUtc,
            LiveSessionDisplayStateCalculator.Compute(context.Status, context.StartsAtUtc, context.EndsAtUtc, now, options.Value.JoinWindowBeforeMinutes),
            context.Status.ToString(),
            context.CancelReason,
            context.RecordingEpisodeId,
            meetUrl,
            meeting is null ? PendingSummary(sessionId) : SessionMeetingService.ToSummary(meeting),
            // The owner is never counted as one of their own learners.
            enrolled.Count(id => id != context.InstructorUserId),
            stats?.ExpectedLearners ?? 0,
            stats?.JoinedLearners ?? 0,
            now);
    }

    /// <summary>
    /// The learners of one session of the caller's: everyone currently enrolled, plus everyone who was ever invited or ever handed the link (so a learner who joined
    /// and later lost access still shows). Filtered, sorted by display name (unknown names last), then paged — only the page is turned into response rows.
    /// </summary>
    public async Task<Result<PagedResult<RosterItem>>> GetRosterAsync(
        Guid userId,
        Guid sessionId,
        RosterFilter filter,
        int page,
        int pageSize,
        CancellationToken cancellationToken)
    {
        var owned = await GetOwnedSessionAsync<PagedResult<RosterItem>>(userId, sessionId, cancellationToken).ConfigureAwait(false);
        if (owned.Failure is { } failure)
        {
            return failure;
        }

        var context = owned.Context!;
        var effectivePage = page < 1 ? 1 : page;
        var effectivePageSize = pageSize < 1 ? DefaultRosterPageSize : Math.Min(pageSize, MaxRosterPageSize);

        var enrolled = await learning.GetActiveEnrolledUserIdsAsync(context.CourseId, cancellationToken).ConfigureAwait(false);
        var inviteStatuses = await invites.GetLearnerStatusesForSessionAsync(sessionId, cancellationToken).ConfigureAwait(false);
        var joinedIds = (await joinLogs.GetJoinedLearnerIdsAsync(sessionId, cancellationToken).ConfigureAwait(false)).ToHashSet();

        // Learners only: the instructor is not on their own roster even if they appear in one of the sources.
        var everyone = new HashSet<Guid>(enrolled);
        everyone.UnionWith(inviteStatuses.Keys);
        everyone.UnionWith(joinedIds);
        everyone.Remove(context.InstructorUserId);

        var selected = filter switch
        {
            RosterFilter.Joined => everyone.Where(joinedIds.Contains).ToList(),
            RosterFilter.NotJoined => everyone.Where(id => !joinedIds.Contains(id)).ToList(),
            _ => everyone.ToList(),
        };

        if (selected.Count == 0)
        {
            return PagedResult<RosterItem>.Create([], 0, effectivePage, effectivePageSize);
        }

        // Names are needed to sort, so contact info is read for the whole selection in one batch; only the page below is projected into rows.
        var contactInfo = await contacts.GetUsersContactInfoAsync(selected, cancellationToken).ConfigureAwait(false);

        var pageIds = selected
            .OrderBy(id => contactInfo.ContainsKey(id) ? 0 : 1)
            .ThenBy(id => contactInfo.TryGetValue(id, out var info) ? info.DisplayName : string.Empty, StringComparer.OrdinalIgnoreCase)
            .ThenBy(id => id)
            .Skip((effectivePage - 1) * effectivePageSize)
            .Take(effectivePageSize)
            .ToList();

        var joinSummaries = pageIds.Count == 0
            ? new Dictionary<Guid, LearnerJoinSummary>()
            : (await joinLogs.GetLearnerJoinSummariesAsync(sessionId, pageIds, cancellationToken).ConfigureAwait(false)).ToDictionary(p => p.Key, p => p.Value);

        var items = pageIds.Select(id =>
        {
            var known = contactInfo.TryGetValue(id, out var info);
            var summary = joinSummaries.GetValueOrDefault(id);

            return new RosterItem(
                id,
                known ? NullIfBlank(info.DisplayName) : null,
                known ? EmailMasker.Mask(info.Email) : null,
                inviteStatuses.TryGetValue(id, out var inviteStatus) ? inviteStatus.ToString() : null,
                Joined: joinedIds.Contains(id),
                FirstJoinedAtUtc: summary?.FirstJoinedAtUtc,
                JoinCount: summary?.JoinCount ?? 0);
        }).ToList();

        return PagedResult<RosterItem>.Create(items, selected.Count, effectivePage, effectivePageSize);
    }

    // ---- Helpers ------------------------------------------------------------------------------------

    /// <summary>The session's context if it exists and belongs to <paramref name="userId"/> (404 if unknown, 403 if someone else's) — never an administrator bypass.</summary>
    private async Task<(LiveSessionContext? Context, Result<T>? Failure)> GetOwnedSessionAsync<T>(
        Guid userId, Guid sessionId, CancellationToken cancellationToken)
    {
        var context = (await schedule.GetSessionContextsAsync([sessionId], cancellationToken).ConfigureAwait(false))
            .FirstOrDefault(c => c.SessionId == sessionId);

        if (context is null)
        {
            return (null, Result.Failure<T>(DomainError.NotFound("ไม่พบคาบสอนนี้")));
        }

        if (context.InstructorUserId != userId)
        {
            return (null, Result.Failure<T>(DomainError.Forbidden("คุณไม่มีสิทธิ์เข้าถึงคาบสอนนี้")));
        }

        return (context, null);
    }

    private static InstructorMeetingSummary MeetingSummaryOf(Guid sessionId, IReadOnlyDictionary<Guid, SESSION_MEETING> rooms) =>
        rooms.TryGetValue(sessionId, out var meeting) ? SessionMeetingService.ToSummary(meeting) : PendingSummary(sessionId);

    /// <summary>A session whose meeting row does not exist yet (it is staged with the session, so this is only a transient or legacy state) reads as "waiting".</summary>
    private static InstructorMeetingSummary PendingSummary(Guid sessionId) =>
        new(sessionId, null, MeetingSyncStatus.Pending, HasMeetingLink: false, IsUsable: false, LastSyncAtUtc: null, MeetingNeedsAction.Waiting, ErrorCode: null);

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;
}
