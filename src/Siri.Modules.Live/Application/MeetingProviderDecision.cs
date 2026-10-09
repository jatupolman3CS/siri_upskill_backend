using Siri.Modules.Live.Domain;

namespace Siri.Modules.Live.Application;

/// <summary>What has to happen next for a session's room, as far as can be told <b>without calling Google</b>.</summary>
public enum MeetingProviderOutcome
{
    /// <summary>An outside call is needed (Google Calendar, or the dev fake provider): the <c>live-meeting-sync</c> job does it.</summary>
    NeedsExternalCall,

    /// <summary>No room can be built automatically: the instructor pastes a link (<c>Manual</c> provider, <c>AwaitingLink</c>).</summary>
    AwaitingLink,

    /// <summary>The instructor's Google connection broke (revoked / expired / too narrow a scope): they must reconnect.</summary>
    NeedsReconnect,
}

/// <param name="Outcome">The decision.</param>
/// <param name="ReconnectReason">The stored short code (one of <see cref="GoogleAccountRevokedReason"/>) — set only for <see cref="MeetingProviderOutcome.NeedsReconnect"/>.</param>
public readonly record struct MeetingProviderDecision(MeetingProviderOutcome Outcome, string? ReconnectReason = null)
{
    public static readonly MeetingProviderDecision NeedsExternalCall = new(MeetingProviderOutcome.NeedsExternalCall);

    public static readonly MeetingProviderDecision AwaitingLink = new(MeetingProviderOutcome.AwaitingLink);

    /// <summary>
    /// The one rule, shared by everyone who has to decide it, for a room that does not exist yet (no Google event, no URL):
    /// <list type="bullet">
    /// <item><c>Logging</c> (development fake) — the job builds the fake room, nothing to decide here.</item>
    /// <item><c>ManualOnly</c> — Google is never called: the instructor pastes a link.</item>
    /// <item>an <b>active</b> Google account — a call to Google is needed: the job does it.</item>
    /// <item>no usable account — if their connection <em>broke</em> (anything but a deliberate disconnect) they have already been told to
    /// reconnect and their classes wait for that; otherwise (never connected, or disconnected on purpose) they paste a link.</item>
    /// </list>
    /// Pure and cheap: it reads only configuration and the account row the caller already loaded — it never touches the network, so the request that
    /// creates the class can decide it in the same transaction (<c>LiveMeetingSink</c>) and the job applies the identical rule to anything still pending.
    /// </summary>
    public static MeetingProviderDecision ForNewRoom(LiveProviderMode mode, INSTRUCTOR_GOOGLE_ACCOUNT? account)
    {
        if (mode == LiveProviderMode.Logging)
        {
            return NeedsExternalCall;
        }

        if (mode == LiveProviderMode.ManualOnly)
        {
            return AwaitingLink;
        }

        if (account is { IsActive: true })
        {
            return NeedsExternalCall;
        }

        return BrokenConnectionReason(account) is { } reason
            ? new MeetingProviderDecision(MeetingProviderOutcome.NeedsReconnect, reason)
            : AwaitingLink;
    }

    /// <summary>The revocation reason when the account was revoked because the connection <em>failed</em> (token rejected, scope too narrow, ...);
    /// <c>null</c> for an active account, no account, or a deliberate disconnect.</summary>
    public static string? BrokenConnectionReason(INSTRUCTOR_GOOGLE_ACCOUNT? account) =>
        account is { IsActive: false, REVOKED_REASON: { } reason } && reason != GoogleAccountRevokedReason.UserDisconnected
            ? reason
            : null;
}
