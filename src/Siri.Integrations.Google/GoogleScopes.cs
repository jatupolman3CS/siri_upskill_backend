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
