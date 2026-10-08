namespace Siri.Modules.Live.Domain;

/// <summary>
/// Lifecycle of a <see cref="SESSION_INVITE"/> (docs/contracts/P11-04-live-invites-ics-reminders.md §2.2).
/// Enum type and members stay PascalCase (docs/DECISIONS.md D-17); stored as a string.
/// </summary>
public enum InviteStatus
{
    /// <summary>The participant should be invited; nothing has been sent yet.</summary>
    Pending,

    /// <summary>An invitation (or an updated one) has been sent.</summary>
    Invited,

    /// <summary>The invitation was withdrawn — session cancelled, or the learner lost access.</summary>
    Cancelled,

    /// <summary>Not invited because there was nothing to send to (e.g. no contact e-mail).</summary>
    Skipped,
}
