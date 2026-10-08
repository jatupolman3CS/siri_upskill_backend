using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;

namespace Siri.Modules.Live.Infrastructure;

/// <summary>
/// The real <see cref="ILiveMeetingReadinessReader"/> (P11-03 contract section 4.2): Catalog's publish gate asks which sessions still
/// lack a usable room. A session with no meeting row, or whose row is not usable (<c>SESSION_MEETING.IsUsable</c> — a room URL exists
/// and the meeting is not deleted), is "not ready". One query for all ids; usability is decided by the entity itself so the rule has a
/// single definition.
/// </summary>
public sealed class LiveMeetingReadinessReader(ISessionMeetingRepository meetings) : ILiveMeetingReadinessReader
{
    public async Task<IReadOnlyCollection<Guid>> GetSessionsWithoutUsableMeetingAsync(
        IReadOnlyCollection<Guid> sessionIds, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(sessionIds);

        var ids = sessionIds.Distinct().ToArray();
        if (ids.Length == 0)
        {
            return [];
        }

        var usable = (await meetings.GetBySessionIdsAsync(ids, cancellationToken).ConfigureAwait(false))
            .Where(m => m.IsUsable)
            .Select(m => m.SESSION_ID)
            .ToHashSet();

        return ids.Where(id => !usable.Contains(id)).ToArray();
    }
}
