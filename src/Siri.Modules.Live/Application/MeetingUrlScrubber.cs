using Siri.SharedKernel;

namespace Siri.Modules.Live.Application;

/// <summary>
/// Last line of defence for the rule that e-mails, calendar files and in-app notifications carry <b>only</b> the platform join
/// URL (docs/contracts/P11-04-live-invites-ics-reminders.md §0 item 3): free text an instructor typed — a session title or
/// description, a cancel reason — is passed through here before it goes into any outbound message, so a pasted
/// Meet/Zoom/Teams link cannot travel to every learner's inbox through a field the platform did not control. Anything that
/// looks like a link to a meeting host (<c>meet.google.com</c>, <c>zoom.us</c>, <c>teams.microsoft.com</c>,
/// <c>teams.live.com</c>, any sub-domain, with or without a scheme — and the disguised forms: zero-width characters, line breaks,
/// full-width characters, percent-encoding) is replaced by a neutral placeholder.
/// <para>
/// The matching itself is <see cref="MeetingLinkText"/> in the SharedKernel — the same rule Catalog applies when it
/// <em>rejects</em> such text on write and when it scrubs the public read model, so the two modules can never disagree about what
/// a meeting link is (and Catalog does not reference Live). The host list always includes the defaults and, on top of them, whatever
/// <c>Live:AllowedMeetingHosts</c> configures — an instructor's link is only ever accepted for those hosts, so those are exactly
/// the hosts a real room URL can have.
/// </para>
/// </summary>
public static class MeetingUrlScrubber
{
    /// <summary>What a removed link is replaced with.</summary>
    public const string Placeholder = MeetingLinkText.Placeholder;

    /// <summary>Hosts that are scrubbed whatever the configuration says.</summary>
    public static IReadOnlyList<string> DefaultHosts => MeetingLinkText.DefaultHosts;

    /// <summary>Returns <paramref name="text"/> with every link to a meeting host replaced by <see cref="Placeholder"/>.</summary>
    /// <param name="text">Free text (may be <c>null</c>).</param>
    /// <param name="extraHosts">Additional configured hosts (<c>Live:AllowedMeetingHosts</c>); the defaults are always included.</param>
    public static string? Scrub(string? text, IEnumerable<string>? extraHosts = null) =>
        MeetingLinkText.Scrub(text, extraHosts);

    /// <summary>The same as <see cref="Scrub"/> for a value that must not become <c>null</c>.</summary>
    public static string ScrubRequired(string text, IEnumerable<string>? extraHosts = null) =>
        MeetingLinkText.ScrubRequired(text, extraHosts);
}
