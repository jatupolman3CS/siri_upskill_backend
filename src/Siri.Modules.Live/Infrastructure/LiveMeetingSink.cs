using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;

namespace Siri.Modules.Live.Infrastructure;

/// <summary>
/// The real <see cref="ILiveMeetingSink"/> (P11-03 contract section 4.4): Catalog's live-session handlers tell Live that a class was
/// scheduled/changed/cancelled, and Live <b>stages</b> the matching <see cref="SESSION_MEETING"/> change on the shared
/// <c>AppDbContext</c>.
/// <para>
/// <b>It must never call <c>SaveChanges</c></b> — the Catalog handler owns the transaction and saves once, so the class and its
/// meeting row commit (or fail) together. It also must not look up the session or its instructor: the handler calls it <em>before</em>
/// the session row exists in the database. Only the meeting row is read (tracked), and a row staged earlier in the same unit of work
/// is found through the change tracker. The sync job later reads the session's real details and builds the room.
/// </para>
/// </summary>
public sealed class LiveMeetingSink(ISessionMeetingRepository meetings) : ILiveMeetingSink
{
    public async Task OnSessionScheduledAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var existing = await meetings.GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (existing is null)
        {
            meetings.Add(SESSION_MEETING.Stage(sessionId));
        }
    }

    public async Task OnSessionChangedAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var meeting = await meetings.GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (meeting is null)
        {
            // A class scheduled before Live existed: it gets its meeting row now.
            meetings.Add(SESSION_MEETING.Stage(sessionId));
            return;
        }

        meeting.MarkSessionChanged();
    }

    public async Task OnSessionCancelledAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var meeting = await meetings.GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
        meeting?.MarkSessionCancelled();
    }
}
