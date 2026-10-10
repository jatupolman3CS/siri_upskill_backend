namespace Siri.Integrations.Google;

/// <summary>Google OAuth scope URLs used by the Live integration, and the rules for judging a granted scope set.</summary>
public static class GoogleScopes
{
    public const string OpenId = "openid";

    public const string Email = "email";

    /// <summary>Narrowest Calendar scope that may create/patch/delete events on calendars the user owns
    /// (listed as accepted by events.insert/patch/delete/get/list in the Calendar API reference). The default.</summary>
    public const string CalendarEventsOwned = "https://www.googleapis.com/auth/calendar.events.owned";

    public const string CalendarEvents = "https://www.googleapis.com/auth/calendar.events";

    public const string Calendar = "https://www.googleapis.com/auth/calendar";

    /// <summary>Read metadata (conference records, recordings) of Meet spaces the user can access. <b>Sensitive</b> scope (P11-13).</summary>
    public const string MeetSpaceReadonly = "https://www.googleapis.com/auth/meetings.space.readonly";

    /// <summary>Read and download the Drive files Google Meet created (recordings). <b>Restricted</b> scope (P11-13): an app that
    /// stores or transmits data obtained with it needs Google's security assessment before it can serve more than 100 users.</summary>
    public const string DriveMeetReadonly = "https://www.googleapis.com/auth/drive.meet.readonly";

    /// <summary>The two scopes the automatic recording import asks for, on top of the calendar scope, in a separate consent step.</summary>
    public static readonly IReadOnlyList<string> RecordingScopes = [MeetSpaceReadonly, DriveMeetReadonly];

    /// <summary>True when the space-delimited <paramref name="scopes"/> contain <b>both</b> recording scopes (exact token match).</summary>
    public static bool HasRecordingScopes(string? scopes)
    {
        var granted = Split(scopes);
        return RecordingScopes.All(required => granted.Contains(required, StringComparer.Ordinal));
    }

    /// <summary>Default <c>Integrations:Google:Scopes</c> when none is configured.</summary>
    public static readonly IReadOnlyList<string> DefaultScopes = [OpenId, Email, CalendarEventsOwned];

    /// <summary>Any one of these satisfies "the instructor granted calendar write access" (P11-03 contract section 3.5).
    /// <c>calendar.app.created</c> is intentionally absent: it only reaches app-created secondary calendars, not <c>primary</c>.</summary>
    public static readonly IReadOnlyList<string> AcceptedCalendarScopes = [CalendarEventsOwned, CalendarEvents, Calendar];

    /// <summary>True when the space-delimited <paramref name="scopes"/> string (the <c>scope</c> member Google
    /// returns, or a configured list joined by spaces) contains at least one accepted calendar scope. Exact token
    /// match, so <c>calendar.readonly</c> or a longer look-alike URL does not count.</summary>
    public static bool HasCalendarScope(string? scopes) =>
        HasCalendarScope(Split(scopes));

    public static bool HasCalendarScope(IEnumerable<string> scopes) =>
        scopes.Any(scope => AcceptedCalendarScopes.Contains(scope, StringComparer.Ordinal));

    public static IReadOnlyList<string> Split(string? scopes) =>
        string.IsNullOrWhiteSpace(scopes)
            ? []
            : scopes.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}
