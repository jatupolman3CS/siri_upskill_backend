using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Siri.Modules.Live.Contracts;
using Siri.Modules.Live.Domain;
using Siri.Persistence;

namespace Siri.Modules.Live.Infrastructure;

/// <summary>
/// The rule <see cref="SESSION_MEETING.IsUsable"/> as an expression EF can translate (the property itself is not mapped, so it cannot appear in a query).
/// A unit test pins this expression to the property for every sync status, so the two cannot drift apart.
/// </summary>
public static class MeetingUsability
{
    public static readonly Expression<Func<SESSION_MEETING, bool>> IsUsable =
        m => m.MEET_URL_ENCRYPTED != null && m.SYNC_STATUS != MeetingSyncStatus.Deleted;
}

/// <summary>
/// Implementation of <see cref="ILiveAttendanceReader"/> (docs/contracts/P11-05-live-learner-instructor-api-join-gate.md section 3.1). Three grouped
/// queries answer any number of sessions at once — never a query per session.
/// </summary>
public sealed class LiveAttendanceReader(AppDbContext context) : ILiveAttendanceReader
{
    public async Task<IReadOnlySet<Guid>> GetCourseIdsAttendedAsync(
        Guid userId,
        IReadOnlyCollection<Guid> courseIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(courseIds);

        var ids = courseIds.Distinct().ToArray();
        if (userId == Guid.Empty || ids.Length == 0)
        {
            return new HashSet<Guid>();
        }

        // Learner role only: an instructor entering their own room is not "attending" for the refund rule. Uses IX_SESSION_JOIN_LOGS_USER_COURSE.
        var attended = await context.SessionJoinLogs()
            .AsNoTracking()
            .Where(l => l.USER_ID == userId && l.ROLE == LiveParticipantRole.Learner && ids.Contains(l.COURSE_ID))
            .Select(l => l.COURSE_ID)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return attended.ToHashSet();
    }

    public async Task<IReadOnlyDictionary<Guid, LiveSessionStats>> GetSessionStatsAsync(
        IReadOnlyCollection<Guid> sessionIds,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);

        var ids = sessionIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return new Dictionary<Guid, LiveSessionStats>();
        }

        var expected = await CountExpectedLearnersAsync(ids, cancellationToken).ConfigureAwait(false);
        var joined = await CountJoinedLearnersAsync(ids, cancellationToken).ConfigureAwait(false);
        var usable = await FindUsableMeetingsAsync(ids, cancellationToken).ConfigureAwait(false);

        return ids.ToDictionary(
            id => id,
            id => new LiveSessionStats(id, expected.GetValueOrDefault(id), joined.GetValueOrDefault(id), usable.Contains(id)));
    }

    // The three grouped reads behind GetSessionStatsAsync. Separate (and internal) so a unit test can prove each one translates to SQL without a database.

    /// <summary>Learners whose invitation is <c>Invited</c>, per session (the instructor's own invite does not count).</summary>
    internal async Task<Dictionary<Guid, int>> CountExpectedLearnersAsync(Guid[] ids, CancellationToken cancellationToken) =>
        await context.SessionInvites()
            .AsNoTracking()
            .Where(i => ids.Contains(i.SESSION_ID) && i.ROLE == LiveParticipantRole.Learner && i.STATUS == InviteStatus.Invited)
            .GroupBy(i => i.SESSION_ID)
            .Select(g => new { SessionId = g.Key, Count = g.Count() })
            .ToDictionaryAsync(x => x.SessionId, x => x.Count, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Distinct learners who were handed the link, per session — a learner who joined three times counts once.</summary>
    internal async Task<Dictionary<Guid, int>> CountJoinedLearnersAsync(Guid[] ids, CancellationToken cancellationToken) =>
        await context.SessionJoinLogs()
            .AsNoTracking()
            .Where(l => ids.Contains(l.SESSION_ID) && l.ROLE == LiveParticipantRole.Learner)
            .GroupBy(l => l.SESSION_ID)
            .Select(g => new { SessionId = g.Key, Count = g.Select(l => l.USER_ID).Distinct().Count() })
            .ToDictionaryAsync(x => x.SessionId, x => x.Count, cancellationToken)
            .ConfigureAwait(false);

    /// <summary>Which of the sessions have a room that can be entered right now.</summary>
    internal async Task<HashSet<Guid>> FindUsableMeetingsAsync(Guid[] ids, CancellationToken cancellationToken) =>
        (await context.SessionMeetings()
            .AsNoTracking()
            .Where(m => ids.Contains(m.SESSION_ID))
            .Where(MeetingUsability.IsUsable)
            .Select(m => m.SESSION_ID)
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false)).ToHashSet();
}
