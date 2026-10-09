using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Domain;
using Siri.SharedKernel;

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
/// <para>
/// <b>The provider decision is made here, not left to the job.</b> Whether a room can be built automatically at all needs no network:
/// it depends on <c>Live:Provider</c> and on whether the instructor has an active Google account — both readable in the same transaction.
/// So when the answer is "no automatic room" (<c>ManualOnly</c>, or no usable Google account) the row is committed straight as
/// <c>Manual</c>/<c>AwaitingLink</c> (or <c>NeedsReconnect</c> after a broken connection): the instructor's screen says "paste the link" at once and the
/// publish gate sees the real state without any background worker running. Only the part that needs Google (creating the Calendar event with
/// its Meet room) is left <c>Pending</c> for <c>live-meeting-sync</c>, which also stays the reconciler for any row that is still pending.
/// The rule itself lives in <see cref="MeetingProviderDecision"/> and is shared with the job.
/// </para>
/// <para>
/// <b>Whose account?</b> The sink is called only from Catalog's live-session handlers, after they have checked that the signed-in user owns the
/// course — so <see cref="IUserContext"/> is the class's instructor. With no signed-in user (nothing today) the decision is simply not made here and the
/// job makes it, exactly as before.
/// </para>
/// </summary>
public sealed class LiveMeetingSink(
    ISessionMeetingRepository meetings,
    IInstructorGoogleAccountRepository googleAccounts,
    IUserContext userContext,
    IOptions<LiveOptions> liveOptions) : ILiveMeetingSink
{
    public async Task OnSessionScheduledAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var meeting = await meetings.GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (meeting is null)
        {
            meeting = SESSION_MEETING.Stage(sessionId);
            meetings.Add(meeting);
        }

        await DecideProviderAsync(meeting, cancellationToken).ConfigureAwait(false);
    }

    public async Task OnSessionChangedAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var meeting = await meetings.GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
        if (meeting is null)
        {
            // A class scheduled before Live existed: it gets its meeting row now.
            meeting = SESSION_MEETING.Stage(sessionId);
            meetings.Add(meeting);
        }
        else
        {
            meeting.MarkSessionChanged();
        }

        await DecideProviderAsync(meeting, cancellationToken).ConfigureAwait(false);
    }

    public async Task OnSessionCancelledAsync(Guid sessionId, CancellationToken cancellationToken)
    {
        var meeting = await meetings.GetBySessionIdAsync(sessionId, cancellationToken).ConfigureAwait(false);
        meeting?.MarkSessionCancelled();
    }

    /// <summary>Settles the no-network part of the provider choice for a room that does not exist yet. Anything that already has a decision, a
    /// Google event or a URL is left exactly as it is — it is in the job's or the instructor's hands.</summary>
    private async Task DecideProviderAsync(SESSION_MEETING meeting, CancellationToken cancellationToken)
    {
        if (meeting.SYNC_STATUS != MeetingSyncStatus.Pending
            || meeting.PROVIDER is not null
            || meeting.PROVIDER_EVENT_ID is not null
            || meeting.MEET_URL_ENCRYPTED is not null)
        {
            return;
        }

        if (userContext.UserId is not { } instructorUserId)
        {
            return; // instructor unknown here: the job decides, as it always did
        }

        var mode = liveOptions.Value.Provider;

        // The account row matters only when Google may be used at all; the other modes are decided from configuration alone.
        var account = mode == LiveProviderMode.GoogleMeet
            ? await googleAccounts.GetByInstructorUserIdAsync(instructorUserId, cancellationToken).ConfigureAwait(false)
            : null;

        var decision = MeetingProviderDecision.ForNewRoom(mode, account);

        // Remember whose class this is (the job does the same on first sight): it is how a later Google reconnect finds the rooms to reset.
        meeting.AssignInstructor(instructorUserId);

        switch (decision.Outcome)
        {
            case MeetingProviderOutcome.AwaitingLink:
                meeting.ResolveAsAwaitingLink();
                break;

            case MeetingProviderOutcome.NeedsReconnect:
                meeting.RecordNeedsReconnect(decision.ReconnectReason!);
                break;

            case MeetingProviderOutcome.NeedsExternalCall:
            default:
                break; // stays Pending: the job calls Google
        }
    }
}
