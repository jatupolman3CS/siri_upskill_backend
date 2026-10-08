using System.Text;
using System.Text.RegularExpressions;
using MimeKit;
using Siri.SharedKernel;

namespace Siri.Integrations.Email;

/// <summary>
/// Turns an <see cref="EmailMessage"/> into the <see cref="MimeMessage"/> that <see cref="SmtpEmailSender"/>
/// sends — kept separate from the SMTP client so the exact MIME structure and every input check can be unit
/// tested without a network (task P11-04, docs/contracts/P11-04-live-invites-ics-reminders.md §3.3).
/// <para>
/// <b>Without a calendar</b> the message is exactly what <see cref="SmtpEmailSender"/> always built: a
/// <see cref="BodyBuilder"/> HTML-only body.
/// </para>
/// <para>
/// <b>With a calendar</b> the body is
/// <code>
/// multipart/mixed
///  ├─ multipart/alternative
///  │    ├─ text/html;     charset=utf-8
///  │    └─ text/calendar; charset=utf-8; method=REQUEST|CANCEL|PUBLISH
///  └─ text/calendar; charset=utf-8; method=...; name="invite.ics"  (Content-Disposition: attachment)
/// </code>
/// The inline alternative is what Gmail/Outlook/Apple Mail turn into an Accept/Add-to-calendar card; the
/// attachment is the fallback for clients that only look at attachments. <see cref="BodyBuilder"/> is
/// deliberately not used for this shape (it cannot express a calendar alternative).
/// </para>
/// <para>
/// <b>Header injection.</b> The only caller-controlled values that reach a header are the recipient, the subject
/// and the calendar method. The recipient must be exactly one well-formed mailbox with no control characters;
/// the subject has every control character (CR, LF, ...) replaced by a space before it is set; the method must be
/// one of three fixed strings. The ICS document itself is a MIME <i>body</i>, so it cannot add headers, and its
/// line endings are normalised to CRLF as RFC 5545 requires. Escaping the free-text <i>inside</i> the ICS is the
/// builder's job, not this class's.
/// </para>
/// </summary>
public static class EmailMimeMessageFactory
{
    /// <summary>Upper bound on the ICS document (characters) — equals the outbox column cap
    /// (<c>EMAIL_OUTBOX_MESSAGE.CalendarIcsMaxLength</c>). The document is carried twice per email (inline and
    /// attachment), so the cap also bounds the message size.</summary>
    public const int MaxCalendarContentLength = 200_000;

    /// <summary>File name of the attached copy of the calendar.</summary>
    public const string CalendarAttachmentFileName = "invite.ics";

    public const string InvalidRecipientCode = "email.invalid_recipient";

    public const string InvalidCalendarCode = "email.invalid_calendar";

    private static readonly string[] AllowedCalendarMethods = ["REQUEST", "CANCEL", "PUBLISH"];

    private static readonly UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    private static readonly Regex AnyLineBreak = new(@"\r\n|\r|\n", RegexOptions.Compiled | RegexOptions.CultureInvariant);

    /// <summary>Builds the message. Returns a failure (and never a half-built message) when the recipient or the
    /// calendar is unacceptable; the caller owns, and must dispose, the returned <see cref="MimeMessage"/>.</summary>
    public static Result<MimeMessage> Create(EmailMessage message, string fromAddress, string fromDisplayName)
    {
        ArgumentNullException.ThrowIfNull(message);

        if (!TryParseRecipient(message.ToAddress, out var recipient))
        {
            return Result.Failure<MimeMessage>(new DomainError(InvalidRecipientCode, "The recipient address is not a single valid mailbox."));
        }

        string? icsContent = null;
        if (message.Calendar is { } calendar)
        {
            var calendarError = ValidateCalendar(calendar);
            if (calendarError is not null)
            {
                return Result.Failure<MimeMessage>(calendarError);
            }

            icsContent = AnyLineBreak.Replace(calendar.IcsContent, "\r\n");
        }

        var mime = new MimeMessage();
        try
        {
            mime.From.Add(new MailboxAddress(fromDisplayName, fromAddress));
            mime.To.Add(recipient);
            mime.Subject = SanitizeHeaderText(message.Subject);

            mime.Body = message.Calendar is null
                ? new BodyBuilder { HtmlBody = message.HtmlBody }.ToMessageBody()
                : BuildCalendarBody(message.HtmlBody, message.Calendar.Method, icsContent!);

            return Result.Success(mime);
        }
        catch
        {
            mime.Dispose();
            throw;
        }
    }

    private static MimeEntity BuildCalendarBody(string htmlBody, string method, string icsContent)
    {
        var html = new TextPart("html");
        html.SetText(Utf8NoBom, htmlBody);

        var alternative = new Multipart("alternative")
        {
            html,
            BuildCalendarPart(method, icsContent, asAttachment: false),
        };

        return new Multipart("mixed")
        {
            alternative,
            BuildCalendarPart(method, icsContent, asAttachment: true),
        };
    }

    private static TextPart BuildCalendarPart(string method, string icsContent, bool asAttachment)
    {
        var part = new TextPart("calendar");

        // TextPart picks the transfer encoding (7bit/quoted-printable/base64) at serialisation time from the
        // content and the SMTP server's capabilities, so Thai text in SUMMARY/DESCRIPTION survives any relay.
        part.SetText(Utf8NoBom, icsContent);
        part.ContentType.Parameters["method"] = method;

        if (asAttachment)
        {
            part.ContentDisposition = new ContentDisposition(ContentDisposition.Attachment);
            part.FileName = CalendarAttachmentFileName; // sets Content-Disposition filename and Content-Type name
        }

        return part;
    }

    private static bool TryParseRecipient(string? toAddress, out MailboxAddress recipient)
    {
        recipient = null!;

        if (string.IsNullOrWhiteSpace(toAddress) || toAddress.Any(char.IsControl))
        {
            return false;
        }

        // MailboxAddress.TryParse accepts exactly one mailbox: a comma-separated list, a group, or trailing
        // text after the address all fail — so one outbox row can never address more than one person.
        if (!MailboxAddress.TryParse(toAddress, out recipient!))
        {
            return false;
        }

        // MimeKit also accepts a bare local part ("someone"); a deliverable address needs a domain, so refuse it
        // here instead of letting the SMTP server reject it after a round trip.
        var at = recipient.Address.LastIndexOf('@');
        return at > 0 && at < recipient.Address.Length - 1;
    }

    private static DomainError? ValidateCalendar(EmailCalendarContent calendar)
    {
        if (calendar.Method is null || Array.IndexOf(AllowedCalendarMethods, calendar.Method) < 0)
        {
            return new DomainError(InvalidCalendarCode, "Calendar method must be REQUEST, CANCEL or PUBLISH.");
        }

        if (string.IsNullOrWhiteSpace(calendar.IcsContent)
            || !calendar.IcsContent.StartsWith("BEGIN:VCALENDAR", StringComparison.Ordinal))
        {
            return new DomainError(InvalidCalendarCode, "Calendar content must be a document starting with BEGIN:VCALENDAR.");
        }

        if (calendar.IcsContent.Length > MaxCalendarContentLength)
        {
            return new DomainError(InvalidCalendarCode, $"Calendar content must be at most {MaxCalendarContentLength} characters.");
        }

        return null;
    }

    /// <summary>Replaces every control character (CR, LF, TAB, NEL, ...) and the Unicode line/paragraph
    /// separators with a space, then trims. A header value can then never contain a line break, however the
    /// mail library would have encoded it.</summary>
    internal static string SanitizeHeaderText(string? value)
    {
        if (string.IsNullOrEmpty(value))
        {
            return string.Empty;
        }

        if (!value.Any(IsHeaderBreakingCharacter))
        {
            return value; // an ordinary subject goes through untouched
        }

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(IsHeaderBreakingCharacter(c) ? ' ' : c);
        }

        return builder.ToString().Trim();
    }

    private static bool IsHeaderBreakingCharacter(char c) => char.IsControl(c) || c is '\u2028' or '\u2029';
}
