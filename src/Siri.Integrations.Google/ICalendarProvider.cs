using Siri.SharedKernel;

namespace Siri.Integrations.Google;

/// <summary>What to put on the instructor's primary calendar for one live session.</summary>
/// <param name="Summary">Event title (<c>{CourseTitle} — {SessionTitle}</c>).</param>
/// <param name="Description">Plain-text body. <c>null</c> omits it on create and clears it on update.</param>
/// <param name="StartsAtUtc">Start instant (UTC).</param>
/// <param name="EndsAtUtc">End instant (UTC), after <paramref name="StartsAtUtc"/>.</param>
/// <param name="RequestId">Idempotency key for <c>conferenceData.createRequest.requestId</c> (<c>meetingId:N</c>). Re-sending
/// the same id never creates a second Meet room.</param>
/// <param name="PrivateSessionId">Value stored in the event's <c>extendedProperties.private.siriSessionId</c> (<c>sessionId:N</c>)
/// so a retry can find an event whose creation response was lost, instead of creating a duplicate.</param>
public sealed record CalendarEventRequest(
    string Summary,
    string? Description,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string RequestId,
    string PrivateSessionId);

/// <summary>An event as Google reports it.</summary>
/// <param name="EventId">Calendar event id.</param>
/// <param name="MeetUrl">The Meet join URL (<c>hangoutLink</c> / video entry point) when Google has produced one and it is an
/// absolute https URL. <b>Sensitive — never log.</b> The Live module must still run it through its allow-list before storing.</param>
/// <param name="ConferencePending">Google is still creating the Meet room (<c>createRequest.status.statusCode = pending</c>);
/// call <see cref="ICalendarProvider.GetEventAsync"/> again shortly. <c>MeetUrl == null &amp;&amp; !ConferencePending</c> after a
/// create means Google produced no room (failure / account without Meet) and should be treated as a failed attempt.</param>
public sealed record CalendarEventResult(string EventId, string? MeetUrl, bool ConferencePending);

/// <summary>
/// Google Calendar v3 operations on the <b>primary</b> calendar of the account the supplied access token belongs to
/// (P11-03 contract section 5, FROZEN). Every method takes the access token per call (no central credential) and returns a
/// typed <see cref="Result"/> with a <see cref="GoogleErrors"/> code; none throws for an expected Google/network failure.
/// All writes pass <c>sendUpdates=none</c> — Google never e-mails anyone; invitations are the platform's job.
/// </summary>
public interface ICalendarProvider
{
    /// <summary>Creates the event with a Google Meet room (<c>conferenceDataVersion=1</c>, <c>createRequest</c>,
    /// <c>conferenceSolutionKey.type=hangoutsMeet</c>). Guests cannot modify/invite/see other guests.</summary>
    Task<Result<CalendarEventResult>> CreateEventWithMeetAsync(string accessToken, CalendarEventRequest request, CancellationToken ct);

    /// <summary>Finds an existing, non-deleted event carrying <c>siriSessionId=<paramref name="privateSessionId"/></c>
    /// (guards against duplicate events when a create's response was lost). Success with <c>null</c> = none.</summary>
    Task<Result<CalendarEventResult?>> FindEventByPrivateSessionIdAsync(string accessToken, string privateSessionId, CancellationToken ct);

    /// <summary>Reads an event (used to poll a <see cref="CalendarEventResult.ConferencePending"/> room).
    /// A cancelled event is reported as <c>google.not_found</c>.</summary>
    Task<Result<CalendarEventResult>> GetEventAsync(string accessToken, string eventId, CancellationToken ct);

    /// <summary>Patches title/description/time of an existing event. Does not re-create the conference. 404/410 =
    /// <c>google.not_found</c> (the caller recreates the event).</summary>
    Task<Result<CalendarEventResult>> UpdateEventAsync(string accessToken, string eventId, CalendarEventRequest request, CancellationToken ct);

    /// <summary>Deletes the event. 404/410 (already gone) is Success.</summary>
    Task<Result> DeleteEventAsync(string accessToken, string eventId, CancellationToken ct);

    /// <summary>Replaces the event's guest list with exactly <paramref name="attendeeEmails"/> (Calendar REST overwrites the whole
    /// <c>attendees</c> array, so this is "set", not add/remove). Existing guests who are still wanted keep their response
    /// status; the organizer entry is always preserved. Blank/malformed addresses are skipped. Used by P11-04.</summary>
    Task<Result> SetAttendeesAsync(string accessToken, string eventId, IReadOnlyCollection<string> attendeeEmails, CancellationToken ct);
}
