using System.Globalization;
using System.Net;
using System.Text;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using Siri.SharedKernel;

namespace Siri.Integrations.Google;

/// <summary>
/// <see cref="ICalendarProvider"/> over plain <see cref="HttpClient"/> against the documented Google Calendar v3 REST API
/// (https://developers.google.com/workspace/calendar/api/v3/reference/events). No Google SDK.
/// <para>
/// Everything targets the <c>primary</c> calendar of the account the per-call access token belongs to. Every write sends
/// <c>sendUpdates=none</c> so Google never e-mails anyone on our behalf. Attendee e-mail addresses, the access token and the
/// Meet URL are never logged (only operation name, HTTP status and Google's sanitised reason token are).
/// </para>
/// </summary>
public sealed class GoogleCalendarProvider : ICalendarProvider
{
    /// <summary>Named <see cref="HttpClient"/> registered by <c>AddGoogleIntegration</c>.</summary>
    public const string HttpClientName = "google-calendar";

    internal const string EventsEndpoint = "https://www.googleapis.com/calendar/v3/calendars/primary/events";

    /// <summary>Calendar time-zone label sent with start/end (the instants themselves are sent as UTC).</summary>
    internal const string CalendarTimeZone = "Asia/Bangkok";

    /// <summary>Private extended property that links an event back to the platform session.</summary>
    internal const string SessionIdProperty = "siriSessionId";

    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<GoogleCalendarProvider> _logger;

    public GoogleCalendarProvider(IHttpClientFactory httpClientFactory, ILogger<GoogleCalendarProvider> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }

    public async Task<Result<CalendarEventResult>> CreateEventWithMeetAsync(
        string accessToken, CalendarEventRequest request, CancellationToken ct)
    {
        const string operation = "events.insert";
        if (!GoogleHttp.IsUsableToken(accessToken))
        {
            return GoogleHttp.InvalidToken<CalendarEventResult>(operation);
        }

        if (ValidateRequest(request, requireIdentifiers: true) is { } invalid)
        {
            return Result.Failure<CalendarEventResult>(invalid);
        }

        var body = BuildEventBody(request, forCreate: true);
        return await GoogleHttp.SendAsync(
            _httpClientFactory,
            HttpClientName,
            _logger,
            operation,
            () => JsonRequest(HttpMethod.Post, $"{EventsEndpoint}?conferenceDataVersion=1&sendUpdates=none", accessToken, body),
            (response, token) => ReadEventAsync(response, operation, token),
            ct).ConfigureAwait(false);
    }

    public async Task<Result<CalendarEventResult?>> FindEventByPrivateSessionIdAsync(
        string accessToken, string privateSessionId, CancellationToken ct)
    {
        const string operation = "events.list";
        if (!GoogleHttp.IsUsableToken(accessToken))
        {
            return GoogleHttp.InvalidToken<CalendarEventResult?>(operation);
        }

        if (string.IsNullOrWhiteSpace(privateSessionId))
        {
            return Result.Failure<CalendarEventResult?>(GoogleErrors.BadRequest("Google events.list was not attempted: a session id is required."));
        }

        var filter = Uri.EscapeDataString($"{SessionIdProperty}={privateSessionId}");
        var url = $"{EventsEndpoint}?privateExtendedProperty={filter}&maxResults=1&showDeleted=false";

        return await GoogleHttp.SendAsync<CalendarEventResult?>(
            _httpClientFactory,
            HttpClientName,
            _logger,
            operation,
            () => JsonRequest(HttpMethod.Get, url, accessToken, body: null),
            async (response, token) =>
            {
                if (!response.IsSuccessStatusCode)
                {
                    return Result.Failure<CalendarEventResult?>(
                        await GoogleHttp.MapCalendarErrorAsync(response, _logger, operation, token).ConfigureAwait(false));
                }

                var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)) as JsonObject;
                if (root?["items"] is not JsonArray items)
                {
                    return Result.Success<CalendarEventResult?>(null);
                }

                foreach (var item in items)
                {
                    var parsed = ParseEvent(item);
                    if (parsed.IsSuccess)
                    {
                        return Result.Success<CalendarEventResult?>(parsed.Value);
                    }
                }

                return Result.Success<CalendarEventResult?>(null);
            },
            ct).ConfigureAwait(false);
    }

    public async Task<Result<CalendarEventResult>> GetEventAsync(string accessToken, string eventId, CancellationToken ct)
    {
        const string operation = "events.get";
        if (!GoogleHttp.IsUsableToken(accessToken))
        {
            return GoogleHttp.InvalidToken<CalendarEventResult>(operation);
        }

        if (string.IsNullOrWhiteSpace(eventId))
        {
            return Result.Failure<CalendarEventResult>(GoogleErrors.BadRequest("Google events.get was not attempted: an event id is required."));
        }

        return await GoogleHttp.SendAsync(
            _httpClientFactory,
            HttpClientName,
            _logger,
            operation,
            () => JsonRequest(HttpMethod.Get, EventUrl(eventId, query: null), accessToken, body: null),
            (response, token) => ReadEventAsync(response, operation, token),
            ct).ConfigureAwait(false);
    }

    public async Task<Result<CalendarEventResult>> UpdateEventAsync(
        string accessToken, string eventId, CalendarEventRequest request, CancellationToken ct)
    {
        const string operation = "events.patch";
        if (!GoogleHttp.IsUsableToken(accessToken))
        {
            return GoogleHttp.InvalidToken<CalendarEventResult>(operation);
        }

        if (string.IsNullOrWhiteSpace(eventId))
        {
            return Result.Failure<CalendarEventResult>(GoogleErrors.BadRequest("Google events.patch was not attempted: an event id is required."));
        }

        if (ValidateRequest(request, requireIdentifiers: false) is { } invalid)
        {
            return Result.Failure<CalendarEventResult>(invalid);
        }

        var body = BuildEventBody(request, forCreate: false);
        return await GoogleHttp.SendAsync(
            _httpClientFactory,
            HttpClientName,
            _logger,
            operation,
            () => JsonRequest(HttpMethod.Patch, EventUrl(eventId, "conferenceDataVersion=1&sendUpdates=none"), accessToken, body),
            (response, token) => ReadEventAsync(response, operation, token),
            ct).ConfigureAwait(false);
    }

    public async Task<Result> DeleteEventAsync(string accessToken, string eventId, CancellationToken ct)
    {
        const string operation = "events.delete";
        if (!GoogleHttp.IsUsableToken(accessToken))
        {
            return Result.Failure(GoogleErrors.Unauthorized($"Google {operation} was not attempted: the access token is missing or malformed."));
        }

        if (string.IsNullOrWhiteSpace(eventId))
        {
            return Result.Failure(GoogleErrors.BadRequest("Google events.delete was not attempted: an event id is required."));
        }

        return await GoogleHttp.SendAsync(
            _httpClientFactory,
            HttpClientName,
            _logger,
            operation,
            () => JsonRequest(HttpMethod.Delete, EventUrl(eventId, "sendUpdates=none"), accessToken, body: null),
            async (response, token) =>
            {
                // 204/200 = deleted. 404/410 = already gone, which is what the caller wanted.
                if (response.IsSuccessStatusCode || response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.Gone)
                {
                    return Result.Success();
                }

                return Result.Failure(await GoogleHttp.MapCalendarErrorAsync(response, _logger, operation, token).ConfigureAwait(false));
            },
            ct).ConfigureAwait(false);
    }

    public async Task<Result> SetAttendeesAsync(
        string accessToken, string eventId, IReadOnlyCollection<string> attendeeEmails, CancellationToken ct)
    {
        const string operation = "events.attendees";
        if (!GoogleHttp.IsUsableToken(accessToken))
        {
            return Result.Failure(GoogleErrors.Unauthorized($"Google {operation} was not attempted: the access token is missing or malformed."));
        }

        if (string.IsNullOrWhiteSpace(eventId))
        {
            return Result.Failure(GoogleErrors.BadRequest("Google events.attendees was not attempted: an event id is required."));
        }

        var wanted = NormalizeEmails(attendeeEmails, out var skipped);
        if (skipped > 0)
        {
            _logger.LogWarning("Google {Operation}: skipped {SkippedCount} blank or malformed attendee address(es).", operation, skipped);
        }

        // Step 1 - read the event. The Calendar REST API replaces the whole attendees array on PATCH, so we need the
        // current list to keep existing guests' response status and the organizer entry.
        var current = await GoogleHttp.SendAsync<JsonObject>(
            _httpClientFactory,
            HttpClientName,
            _logger,
            "events.get",
            () => JsonRequest(HttpMethod.Get, EventUrl(eventId, query: null), accessToken, body: null),
            async (response, token) =>
            {
                if (!response.IsSuccessStatusCode)
                {
                    return Result.Failure<JsonObject>(
                        await GoogleHttp.MapCalendarErrorAsync(response, _logger, "events.get", token).ConfigureAwait(false));
                }

                var root = JsonNode.Parse(await response.Content.ReadAsStringAsync(token).ConfigureAwait(false)) as JsonObject;
                if (root is null)
                {
                    return Result.Failure<JsonObject>(GoogleErrors.Transient("Google events.get returned an unexpected response."));
                }

                // A cancelled event cannot take guests.
                return GoogleHttp.GetString(root["status"]) == "cancelled"
                    ? Result.Failure<JsonObject>(GoogleErrors.NotFound("Google events.get: the event is cancelled."))
                    : Result.Success(root);
            },
            ct).ConfigureAwait(false);

        if (current.IsFailure)
        {
            return Result.Failure(current.Error);
        }

        var merged = MergeAttendees(current.Value["attendees"] as JsonArray, wanted, out var unchanged);
        if (unchanged)
        {
            return Result.Success();
        }

        // Step 2 - overwrite the array.
        var patch = new JsonObject { ["attendees"] = merged };
        return await GoogleHttp.SendAsync(
            _httpClientFactory,
            HttpClientName,
            _logger,
            "events.patch",
            () => JsonRequest(HttpMethod.Patch, EventUrl(eventId, "sendUpdates=none"), accessToken, patch),
            async (response, token) =>
            {
                if (response.IsSuccessStatusCode)
                {
                    return Result.Success();
                }

                return Result.Failure(await GoogleHttp.MapCalendarErrorAsync(response, _logger, "events.patch", token).ConfigureAwait(false));
            },
            ct).ConfigureAwait(false);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Request building
    // ------------------------------------------------------------------------------------------------------------------

    private static string EventUrl(string eventId, string? query) =>
        $"{EventsEndpoint}/{Uri.EscapeDataString(eventId)}{(query is null ? string.Empty : "?" + query)}";

    private static HttpRequestMessage JsonRequest(HttpMethod method, string url, string accessToken, JsonObject? body)
    {
        var request = new HttpRequestMessage(method, url);
        request.Headers.Accept.Add(new("application/json"));
        GoogleHttp.SetBearer(request, accessToken);
        if (body is not null)
        {
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");
        }

        return request;
    }

    private static DomainError? ValidateRequest(CalendarEventRequest request, bool requireIdentifiers)
    {
        if (string.IsNullOrWhiteSpace(request.Summary))
        {
            return GoogleErrors.BadRequest("Google calendar request was not sent: the event summary is required.");
        }

        if (request.EndsAtUtc <= request.StartsAtUtc)
        {
            return GoogleErrors.BadRequest("Google calendar request was not sent: the event must end after it starts.");
        }

        if (requireIdentifiers && (string.IsNullOrWhiteSpace(request.RequestId) || string.IsNullOrWhiteSpace(request.PrivateSessionId)))
        {
            return GoogleErrors.BadRequest("Google calendar request was not sent: request id and session id are required.");
        }

        return null;
    }

    /// <summary>
    /// Create: summary + (optional) description + UTC start/end labelled <c>Asia/Bangkok</c> + the Meet <c>createRequest</c> +
    /// guest restrictions + the private <c>siriSessionId</c>. Update (PATCH): only the fields the caller owns - summary,
    /// description (cleared when null), start, end - never the conference or guest flags.
    /// </summary>
    internal static JsonObject BuildEventBody(CalendarEventRequest request, bool forCreate)
    {
        var body = new JsonObject
        {
            ["summary"] = request.Summary,
            ["start"] = EventTime(request.StartsAtUtc),
            ["end"] = EventTime(request.EndsAtUtc),
        };

        if (!forCreate)
        {
            body["description"] = request.Description ?? string.Empty;
            return body;
        }

        if (request.Description is not null)
        {
            body["description"] = request.Description;
        }

        body["conferenceData"] = new JsonObject
        {
            ["createRequest"] = new JsonObject
            {
                ["requestId"] = request.RequestId,
                ["conferenceSolutionKey"] = new JsonObject { ["type"] = "hangoutsMeet" },
            },
        };
        body["guestsCanModify"] = false;
        body["guestsCanInviteOthers"] = false;
        body["guestsCanSeeOtherGuests"] = false;
        body["anyoneCanAddSelf"] = false;
        body["extendedProperties"] = new JsonObject
        {
            ["private"] = new JsonObject { [SessionIdProperty] = request.PrivateSessionId },
        };

        return body;
    }

    private static JsonObject EventTime(DateTime utc) => new()
    {
        ["dateTime"] = ToRfc3339Utc(utc),
        ["timeZone"] = CalendarTimeZone,
    };

    private static string ToRfc3339Utc(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        return utc.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", CultureInfo.InvariantCulture);
    }

    // ------------------------------------------------------------------------------------------------------------------
    // Response parsing
    // ------------------------------------------------------------------------------------------------------------------

    private async Task<Result<CalendarEventResult>> ReadEventAsync(HttpResponseMessage response, string operation, CancellationToken ct)
    {
        if (!response.IsSuccessStatusCode)
        {
            return Result.Failure<CalendarEventResult>(
                await GoogleHttp.MapCalendarErrorAsync(response, _logger, operation, ct).ConfigureAwait(false));
        }

        var parsed = ParseEvent(JsonNode.Parse(await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false)));
        if (parsed.IsFailure && parsed.Error.Code == GoogleErrors.TransientCode)
        {
            _logger.LogWarning("Google {Operation} returned a 2xx body without an event id.", operation);
        }

        return parsed;
    }

    /// <summary>Reads id, Meet URL and conference status from an Event resource. A <c>cancelled</c> event is reported as
    /// <c>google.not_found</c> (it is deleted as far as the platform is concerned).</summary>
    internal static Result<CalendarEventResult> ParseEvent(JsonNode? node)
    {
        if (node is not JsonObject root || GoogleHttp.GetString(root["id"]) is not { Length: > 0 } eventId)
        {
            return Result.Failure<CalendarEventResult>(GoogleErrors.Transient("Google returned an unexpected event response."));
        }

        if (GoogleHttp.GetString(root["status"]) == "cancelled")
        {
            return Result.Failure<CalendarEventResult>(GoogleErrors.NotFound("Google event is cancelled."));
        }

        var meetUrl = ExtractMeetUrl(root);
        var pending = meetUrl is null
            && GoogleHttp.GetString(root["conferenceData"]?["createRequest"]?["status"]?["statusCode"]) == "pending";

        return Result.Success(new CalendarEventResult(eventId, meetUrl, pending));
    }

    /// <summary><c>hangoutLink</c>, else the <c>video</c> entry point. Only an absolute https URL is accepted (defence in depth; the
    /// Live module still applies its host allow-list before storing it).</summary>
    private static string? ExtractMeetUrl(JsonObject root)
    {
        var candidate = GoogleHttp.GetString(root["hangoutLink"]);
        if (IsAbsoluteHttps(candidate))
        {
            return candidate;
        }

        if (root["conferenceData"]?["entryPoints"] is JsonArray entryPoints)
        {
            foreach (var entry in entryPoints)
            {
                if (GoogleHttp.GetString(entry?["entryPointType"]) == "video" && GoogleHttp.GetString(entry?["uri"]) is { } uri && IsAbsoluteHttps(uri))
                {
                    return uri;
                }
            }
        }

        return null;
    }

    private static bool IsAbsoluteHttps(string? value) =>
        !string.IsNullOrWhiteSpace(value)
        && Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && uri.Scheme == Uri.UriSchemeHttps;

    // ------------------------------------------------------------------------------------------------------------------
    // Attendees
    // ------------------------------------------------------------------------------------------------------------------

    /// <summary>Trim, drop blank/malformed, de-duplicate case-insensitively (first spelling wins), keep order.</summary>
    internal static List<string> NormalizeEmails(IReadOnlyCollection<string> emails, out int skipped)
    {
        skipped = 0;
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var result = new List<string>();

        foreach (var raw in emails)
        {
            var email = raw?.Trim();
            if (!LooksLikeEmail(email))
            {
                skipped++;
                continue;
            }

            if (seen.Add(email!))
            {
                result.Add(email!);
            }
        }

        return result;
    }

    private static bool LooksLikeEmail(string? value)
    {
        if (string.IsNullOrEmpty(value) || value.Length > 320)
        {
            return false;
        }

        var at = value.IndexOf('@');
        if (at <= 0 || at != value.LastIndexOf('@') || at == value.Length - 1)
        {
            return false;
        }

        return !value.Any(c => char.IsWhiteSpace(c) || char.IsControl(c) || c is ',' or ';' or '<' or '>' or '"');
    }

    /// <summary>
    /// Builds the replacement <c>attendees</c> array: every wanted address (an existing guest keeps its writable fields such as
    /// <c>responseStatus</c>; a new one is just <c>{email}</c>) plus the organizer entry, which is always preserved. Existing
    /// guests no longer wanted are dropped. <paramref name="unchanged"/> is true when the result has exactly the same addresses
    /// as the event already has, so the caller can skip the PATCH.
    /// </summary>
    internal static JsonArray MergeAttendees(JsonArray? existing, IReadOnlyList<string> wanted, out bool unchanged)
    {
        var existingByEmail = new Dictionary<string, JsonObject>(StringComparer.OrdinalIgnoreCase);
        var organizerEntries = new List<JsonObject>();

        if (existing is not null)
        {
            foreach (var item in existing)
            {
                if (item is not JsonObject attendee || GoogleHttp.GetString(attendee["email"]) is not { Length: > 0 } email)
                {
                    continue;
                }

                existingByEmail.TryAdd(email, attendee);
                if (attendee["organizer"] is JsonValue flag && flag.TryGetValue<bool>(out var isOrganizer) && isOrganizer)
                {
                    organizerEntries.Add(attendee);
                }
            }
        }

        var merged = new JsonArray();
        var included = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var attendee in organizerEntries)
        {
            var email = GoogleHttp.GetString(attendee["email"])!;
            if (included.Add(email))
            {
                merged.Add(WritableAttendee(attendee));
            }
        }

        foreach (var email in wanted)
        {
            if (!included.Add(email))
            {
                continue;
            }

            merged.Add(existingByEmail.TryGetValue(email, out var current)
                ? WritableAttendee(current)
                : new JsonObject { ["email"] = email });
        }

        unchanged = included.SetEquals(existingByEmail.Keys);
        return merged;
    }

    /// <summary>Copies only the attendee members a client may write; read-only ones (<c>id</c>, <c>self</c>, <c>organizer</c>) are left out.</summary>
    private static JsonObject WritableAttendee(JsonObject source)
    {
        var copy = new JsonObject();
        foreach (var name in new[] { "email", "displayName", "optional", "responseStatus", "comment", "additionalGuests", "resource" })
        {
            if (source[name] is { } value)
            {
                copy[name] = value.DeepClone();
            }
        }

        return copy;
    }
}
