using Microsoft.Extensions.Logging;
using Siri.SharedKernel;

namespace Siri.Integrations.Google.Logging;

/// <summary>
/// <b>DEVELOPMENT ONLY.</b> A fake <see cref="ICalendarProvider"/> that never contacts Google and "creates" a Meet room that cannot
/// exist: <c>https://meet.invalid/dev/{requestId}</c> (the <c>.invalid</c> TLD is reserved by RFC 2606, so it can never resolve to a real
/// host). Selected exclusively by the explicit setting <c>Live:Provider=Logging</c>; never a default; refused in Production.
/// <para>
/// Stateless and deterministic: the same <c>requestId</c> always yields the same event id and URL, so a retried create never "duplicates".
/// Only counts and ids are logged - never an e-mail address or a URL.
/// </para>
/// <para>
/// Note for the Live module: the fake URL host (<c>meet.invalid</c>) is not in the default <c>Live:AllowedMeetingHosts</c>; the sync job must
/// not run the Google-URL allow-list check against a <c>Logging</c> provider's URL (or the developer must add the host).
/// </para>
/// </summary>
public sealed class LoggingCalendarProvider : ICalendarProvider
{
    public const string DevEventIdPrefix = "dev-";

    public const string DevMeetUrlPrefix = "https://meet.invalid/dev/";

    private readonly ILogger<LoggingCalendarProvider> _logger;

    public LoggingCalendarProvider(ILogger<LoggingCalendarProvider> logger)
    {
        _logger = logger;
        _logger.LogWarning(
            "LoggingCalendarProvider is active (Live:Provider=Logging): Google Calendar/Meet is FAKED. Development only - never use in production.");
    }

    public Task<Result<CalendarEventResult>> CreateEventWithMeetAsync(string accessToken, CalendarEventRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.RequestId))
        {
            return Task.FromResult(Result.Failure<CalendarEventResult>(GoogleErrors.BadRequest("Fake Google create: a request id is required.")));
        }

        var result = ResultFor($"{DevEventIdPrefix}{request.RequestId}");
        _logger.LogInformation("Fake Google create: event {EventId}.", result.EventId);
        return Task.FromResult(Result.Success(result));
    }

    public Task<Result<CalendarEventResult?>> FindEventByPrivateSessionIdAsync(string accessToken, string privateSessionId, CancellationToken ct) =>
        Task.FromResult(Result.Success<CalendarEventResult?>(null));

    public Task<Result<CalendarEventResult>> GetEventAsync(string accessToken, string eventId, CancellationToken ct) =>
        Task.FromResult(KnownEvent(eventId));

    public Task<Result<CalendarEventResult>> UpdateEventAsync(string accessToken, string eventId, CalendarEventRequest request, CancellationToken ct)
    {
        var result = KnownEvent(eventId);
        if (result.IsSuccess)
        {
            _logger.LogInformation("Fake Google update: event {EventId}.", eventId);
        }

        return Task.FromResult(result);
    }

    public Task<Result> DeleteEventAsync(string accessToken, string eventId, CancellationToken ct)
    {
        _logger.LogInformation("Fake Google delete: event {EventId}.", eventId);
        return Task.FromResult(Result.Success());
    }

    public Task<Result> SetAttendeesAsync(string accessToken, string eventId, IReadOnlyCollection<string> attendeeEmails, CancellationToken ct)
    {
        _logger.LogInformation("Fake Google attendees: event {EventId} now has {AttendeeCount} guest(s).", eventId, attendeeEmails.Count);
        return Task.FromResult(Result.Success());
    }

    private static Result<CalendarEventResult> KnownEvent(string eventId) =>
        eventId is { Length: > 0 } && eventId.StartsWith(DevEventIdPrefix, StringComparison.Ordinal)
            ? Result.Success(ResultFor(eventId))
            : Result.Failure<CalendarEventResult>(GoogleErrors.NotFound("Fake Google: unknown event."));

    private static CalendarEventResult ResultFor(string eventId) =>
        new(eventId, $"{DevMeetUrlPrefix}{Uri.EscapeDataString(eventId[DevEventIdPrefix.Length..])}", ConferencePending: false);
}
