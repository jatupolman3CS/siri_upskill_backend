using System.Globalization;
using System.Text;

namespace Siri.Modules.Live.Application;

/// <summary>The iTIP <c>METHOD</c> of a generated calendar document (RFC 5546).</summary>
public enum IcsMethod
{
    /// <summary>A plain publication of events (no attendee) — the purchase-day batch and the downloadable <c>calendar.ics</c>.</summary>
    Publish,

    /// <summary>An invitation or an update (same UID, higher SEQUENCE) addressed to one attendee.</summary>
    Request,

    /// <summary>A withdrawal of an earlier invitation (same UID, higher SEQUENCE) addressed to one attendee.</summary>
    Cancel,
}

/// <summary>
/// One live session as a calendar event. <paramref name="JoinUrl"/> is the <b>platform's</b> join link
/// (<c>{Live:PublicBaseUrl}/live/{sessionId}/join</c>) — the builder has no parameter for, and never sees, the real
/// meeting-room URL (docs/contracts/P11-04-live-invites-ics-reminders.md §4.1).
/// </summary>
public sealed record IcsEvent(
    Guid SessionId,
    int Sequence,
    string Summary,
    string? Description,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    string JoinUrl,
    bool Cancelled);

/// <summary>
/// Hand-written RFC 5545/5546 iCalendar generator (no NuGet dependency, contract P11-04 F6). Pure and static, so every rule is
/// unit-testable without a database: the output is a CRLF-terminated document with 75-octet folded lines, TEXT values escaped
/// (backslash, semicolon, comma, newline) and control characters dropped, so no user-controlled value — a course or session
/// title, a name — can start a new content line (property injection).
/// <list type="bullet">
/// <item><c>UID</c> is <c>{sessionId:N}@{uidHost}</c> for every method, so PUBLISH, REQUEST and CANCEL of one session update the
/// same calendar entry.</item>
/// <item><c>DTSTART</c>/<c>DTEND</c> are UTC (<c>…Z</c>) — the same instant everywhere, no <c>VTIMEZONE</c> needed
/// (Outlook rejects a <c>TZID</c> without one); <c>X-WR-TIMEZONE:Asia/Bangkok</c> is only a display hint.</item>
/// <item><c>PUBLISH</c> carries no <c>ATTENDEE</c>; <c>REQUEST</c>/<c>CANCEL</c> carry exactly one.</item>
/// <item>The document contains the <b>platform join URL only</b> — <c>LOCATION</c>, <c>URL</c> and <c>DESCRIPTION</c> all use
/// <see cref="IcsEvent.JoinUrl"/>.</item>
/// </list>
/// </summary>
public static class IcsCalendarBuilder
{
    /// <summary>The <c>PRODID</c> written on every document.</summary>
    public const string ProductId = "-//SIRI UpSkill//Live Sessions//TH";

    /// <summary>Default for <c>joinOpensMinutesBefore</c> — matches <c>LiveOptions.JoinWindowBeforeMinutes</c>' default.</summary>
    public const int DefaultJoinOpensMinutesBefore = 15;

    /// <summary>Maximum content-line length in octets (excluding the CRLF) before it is folded — RFC 5545 section 3.1.</summary>
    public const int MaxLineOctets = 75;

    /// <summary>Cap on the plain-text session description copied into <c>DESCRIPTION</c>, so one very long description cannot
    /// push a 50-event batch over the outbox's 200,000-character limit.</summary>
    public const int MaxDescriptionCharacters = 1000;

    private const string Crlf = "\r\n";

    /// <summary>U+2028 / U+2029 are written as numeric constants — they are line terminators to some tools, even inside a
    /// character literal.</summary>
    private const char LineSeparator = (char)0x2028;

    private const char ParagraphSeparator = (char)0x2029;

    /// <summary>
    /// Builds the calendar document. <paramref name="attendeeEmail"/> is required for <see cref="IcsMethod.Request"/> and
    /// <see cref="IcsMethod.Cancel"/> and ignored for <see cref="IcsMethod.Publish"/>. <paramref name="joinOpensMinutesBefore"/>
    /// (optional, appended after the contract's parameters so existing callers compile) is the number written into the
    /// "enter the room N minutes early" sentence.
    /// </summary>
    /// <exception cref="ArgumentException">No events, an event with a non-http(s) join URL, a malformed e-mail address, or a
    /// missing attendee for REQUEST/CANCEL.</exception>
    public static string Build(
        IcsMethod method,
        IReadOnlyList<IcsEvent> events,
        string organizerEmail,
        string organizerName,
        string uidHost,
        string? attendeeEmail,
        string? attendeeName,
        DateTime nowUtc,
        int joinOpensMinutesBefore = DefaultJoinOpensMinutesBefore)
    {
        ArgumentNullException.ThrowIfNull(events);

        if (events.Count == 0)
        {
            throw new ArgumentException("A calendar needs at least one event.", nameof(events));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(uidHost);
        EnsureMailbox(organizerEmail, nameof(organizerEmail));

        var includeAttendee = method != IcsMethod.Publish;
        if (includeAttendee)
        {
            if (string.IsNullOrWhiteSpace(attendeeEmail))
            {
                throw new ArgumentException($"A {method} calendar needs the attendee's e-mail address.", nameof(attendeeEmail));
            }

            EnsureMailbox(attendeeEmail, nameof(attendeeEmail));
        }

        var lines = new List<string>(8 + (events.Count * 20))
        {
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            $"PRODID:{ProductId}",
            "CALSCALE:GREGORIAN",
            $"METHOD:{MethodToken(method)}",
            "X-WR-TIMEZONE:Asia/Bangkok",
        };

        var stamp = FormatUtc(nowUtc);
        foreach (var calendarEvent in events)
        {
            AppendEvent(lines, method, calendarEvent, organizerEmail, organizerName, uidHost, includeAttendee ? attendeeEmail : null, attendeeName, stamp, joinOpensMinutesBefore);
        }

        lines.Add("END:VCALENDAR");

        var builder = new StringBuilder();
        foreach (var line in lines)
        {
            AppendFolded(builder, line);
        }

        return builder.ToString();
    }

    /// <summary>The <c>METHOD</c> token (<c>PUBLISH</c>/<c>REQUEST</c>/<c>CANCEL</c>) — equal to the method string the outbox
    /// stores next to the document.</summary>
    public static string MethodToken(IcsMethod method) => method switch
    {
        IcsMethod.Publish => "PUBLISH",
        IcsMethod.Request => "REQUEST",
        IcsMethod.Cancel => "CANCEL",
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "Unknown iCalendar method."),
    };

    /// <summary>The UID shared by every document that mentions this session.</summary>
    public static string BuildUid(Guid sessionId, string uidHost) => $"{sessionId:N}@{uidHost}";

    private static void AppendEvent(
        List<string> lines,
        IcsMethod method,
        IcsEvent calendarEvent,
        string organizerEmail,
        string organizerName,
        string uidHost,
        string? attendeeEmail,
        string? attendeeName,
        string stamp,
        int joinOpensMinutesBefore)
    {
        ArgumentNullException.ThrowIfNull(calendarEvent);

        if (calendarEvent.Sequence < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(calendarEvent), "SEQUENCE cannot be negative.");
        }

        var joinUrl = RequireWebUrl(calendarEvent.JoinUrl);
        var cancelled = calendarEvent.Cancelled || method == IcsMethod.Cancel;

        lines.Add("BEGIN:VEVENT");
        lines.Add($"UID:{BuildUid(calendarEvent.SessionId, uidHost)}");
        lines.Add($"DTSTAMP:{stamp}");
        lines.Add($"SEQUENCE:{calendarEvent.Sequence.ToString(CultureInfo.InvariantCulture)}");
        lines.Add($"DTSTART:{FormatUtc(calendarEvent.StartsAtUtc)}");
        lines.Add($"DTEND:{FormatUtc(calendarEvent.EndsAtUtc)}");
        lines.Add($"SUMMARY:{EscapeText(calendarEvent.Summary)}");
        lines.Add($"DESCRIPTION:{EscapeText(BuildDescription(calendarEvent.Description, joinUrl, joinOpensMinutesBefore))}");
        lines.Add($"LOCATION:{EscapeText($"ออนไลน์ — {joinUrl}")}");
        lines.Add($"URL:{joinUrl}");
        lines.Add($"ORGANIZER;CN={QuoteParameter(organizerName)}:mailto:{organizerEmail.Trim()}");

        if (attendeeEmail is not null)
        {
            lines.Add(
                $"ATTENDEE;CN={QuoteParameter(string.IsNullOrWhiteSpace(attendeeName) ? attendeeEmail : attendeeName)};ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=FALSE:mailto:{attendeeEmail.Trim()}");
        }

        lines.Add("TRANSP:OPAQUE");
        lines.Add(cancelled ? "STATUS:CANCELLED" : "STATUS:CONFIRMED");

        if (!cancelled)
        {
            lines.Add("BEGIN:VALARM");
            lines.Add("TRIGGER:-PT15M");
            lines.Add("ACTION:DISPLAY");
            lines.Add("DESCRIPTION:ใกล้ถึงเวลาเรียนสด");
            lines.Add("END:VALARM");
        }

        lines.Add("END:VEVENT");
    }

    private static string BuildDescription(string? description, string joinUrl, int joinOpensMinutesBefore)
    {
        var minutes = Math.Max(joinOpensMinutesBefore, 0);
        var builder = new StringBuilder();
        builder.Append(CultureInfo.InvariantCulture, $"เข้าห้องผ่านแพลตฟอร์มก่อนเวลา {minutes} นาที: {joinUrl}");

        if (!string.IsNullOrWhiteSpace(description))
        {
            var plain = description.Trim();
            if (plain.Length > MaxDescriptionCharacters)
            {
                plain = CutAtCodePoint(plain, MaxDescriptionCharacters);
            }

            builder.Append("\n\n").Append(plain);
        }

        return builder.ToString();
    }

    /// <summary>RFC 5545 TEXT escaping: backslash, semicolon, comma, newline (CR, LF and CRLF all become <c>\n</c>); every other
    /// control character is dropped (a TAB becomes a space) so the value can never carry a line break of its own.</summary>
    internal static string EscapeText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length + 8);
        for (var i = 0; i < value.Length; i++)
        {
            var c = value[i];
            switch (c)
            {
                case '\\':
                    builder.Append("\\\\");
                    break;
                case ';':
                    builder.Append("\\;");
                    break;
                case ',':
                    builder.Append("\\,");
                    break;
                case '\r':
                    builder.Append("\\n");
                    if (i + 1 < value.Length && value[i + 1] == '\n')
                    {
                        i++; // CRLF is one line break
                    }

                    break;
                case '\n':
                    builder.Append("\\n");
                    break;
                case '\t':
                    builder.Append(' ');
                    break;
                case LineSeparator:
                case ParagraphSeparator:
                    builder.Append("\\n");
                    break;
                default:
                    if (!char.IsControl(c))
                    {
                        builder.Append(c);
                    }

                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>A property-parameter value (the <c>CN</c> of ORGANIZER/ATTENDEE): control characters dropped, double quotes
    /// replaced (a quoted-string cannot contain one), and the whole value wrapped in quotes so <c>;</c> <c>:</c> <c>,</c>
    /// inside a name cannot end the parameter.</summary>
    internal static string QuoteParameter(string? value)
    {
        var builder = new StringBuilder((value?.Length ?? 0) + 2);
        builder.Append('"');
        foreach (var c in value ?? string.Empty)
        {
            if (c == '"')
            {
                builder.Append('\'');
            }
            else if (c == '\t')
            {
                builder.Append(' ');
            }
            else if (!char.IsControl(c) && c is not (LineSeparator or ParagraphSeparator))
            {
                builder.Append(c);
            }
        }

        builder.Append('"');
        return builder.ToString();
    }

    internal static string FormatUtc(DateTime value)
    {
        var utc = value.Kind switch
        {
            DateTimeKind.Utc => value,
            DateTimeKind.Local => value.ToUniversalTime(),
            _ => DateTime.SpecifyKind(value, DateTimeKind.Utc),
        };

        return utc.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);
    }

    /// <summary>Appends <paramref name="line"/> folded to at most <see cref="MaxLineOctets"/> UTF-8 octets per physical line
    /// (continuations start with one space, which counts toward the limit), never splitting a code point, each line ending in
    /// CRLF.</summary>
    internal static void AppendFolded(StringBuilder output, string line)
    {
        var limit = MaxLineOctets;
        var used = 0;

        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (used + size > limit)
            {
                output.Append(Crlf).Append(' ');
                used = 1; // the leading space of the continuation line
            }

            output.Append(rune.ToString());
            used += size;
        }

        output.Append(Crlf);
    }

    private static string RequireWebUrl(string? joinUrl)
    {
        if (string.IsNullOrWhiteSpace(joinUrl)
            || joinUrl.Any(c => char.IsControl(c) || char.IsWhiteSpace(c))
            || !Uri.TryCreate(joinUrl, UriKind.Absolute, out var uri)
            || (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp))
        {
            throw new ArgumentException("The join URL must be an absolute http(s) URL without whitespace.", nameof(joinUrl));
        }

        return joinUrl;
    }

    private static void EnsureMailbox(string? email, string parameterName)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("An e-mail address is required.", parameterName);
        }

        var trimmed = email.Trim();
        var at = trimmed.LastIndexOf('@');
        if (at <= 0
            || at == trimmed.Length - 1
            || trimmed.Any(c => char.IsControl(c) || char.IsWhiteSpace(c) || c is '<' or '>' or '"' or ';' or ','))
        {
            throw new ArgumentException("The e-mail address is not a single plain mailbox.", parameterName);
        }
    }

    private static string CutAtCodePoint(string value, int maxCharacters)
    {
        if (value.Length <= maxCharacters)
        {
            return value;
        }

        var end = maxCharacters;
        if (char.IsHighSurrogate(value[end - 1]))
        {
            end--;
        }

        return value[..end];
    }
}
