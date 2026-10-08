using System.Text;
using MimeKit;
using Siri.Integrations.Email;

namespace Siri.UnitTests.Email;

/// <summary>
/// Parse-back tests for the MIME structure <see cref="EmailMimeMessageFactory"/> builds (task P11-04,
/// docs/contracts/P11-04-live-invites-ics-reminders.md §3.3): every assertion reads the message back out of its
/// serialised bytes with <see cref="MimeMessage.Load(Stream, CancellationToken)"/>, so what is checked is what a
/// mail client would actually receive — not what the builder intended. No network is used.
/// </summary>
public class EmailMimeMessageFactoryTests
{
    private const string From = "no-reply@siriupskill.test";

    private const string FromName = "SIRI UpSkill";

    private const string To = "learner@example.test";

    private const string Html = "<h2>ยืนยันตารางเรียนสด</h2><p>คาบที่ 1 — พฤหัสบดีที่ 1 ตุลาคม 2569</p>";

    /// <summary>A realistic iTIP document (Thai summary long enough to need RFC 5545 line folding, UTC times,
    /// platform join link — never a meeting-room URL), with CRLF line endings as the spec requires.</summary>
    private static string SampleIcs(string method = "REQUEST") =>
        string.Join(
            "\r\n",
            "BEGIN:VCALENDAR",
            "VERSION:2.0",
            "PRODID:-//SIRI UpSkill//Live Sessions//TH",
            "CALSCALE:GREGORIAN",
            $"METHOD:{method}",
            "X-WR-TIMEZONE:Asia/Bangkok",
            "BEGIN:VEVENT",
            "UID:0198a1b2c3d47e5f8a9b0c1d2e3f4a5b@siriupskill.test",
            "DTSTAMP:20261001T030000Z",
            "SEQUENCE:0",
            "DTSTART:20261001T030000Z",
            "DTEND:20261001T050000Z",
            "SUMMARY:คาบเรียนสด: การเขียนโปรแกรมเชิงวัตถุสำหรับผู้เริ่มต้นและผู้ที่ต้องการทบทวนพื้นฐาน",
            " ทั้งหมดอย่างเป็นระบบ",
            "LOCATION:ออนไลน์ — https://siriupskill.test/live/0198a1b2c3d47e5f8a9b0c1d2e3f4a5b/join",
            "URL:https://siriupskill.test/live/0198a1b2c3d47e5f8a9b0c1d2e3f4a5b/join",
            "STATUS:CONFIRMED",
            "END:VEVENT",
            "END:VCALENDAR",
            string.Empty);

    private static EmailMessage Plain(string toAddress = To, string subject = "ยืนยันตารางเรียนสด") =>
        new(toAddress, subject, Html);

    private static EmailMessage WithCalendar(string method = "REQUEST", string? ics = null, string toAddress = To, string subject = "ยืนยันตารางเรียนสด") =>
        new(toAddress, subject, Html, new EmailCalendarContent(method, ics ?? SampleIcs(method)));

    private static MimeMessage Build(EmailMessage message)
    {
        var result = EmailMimeMessageFactory.Create(message, From, FromName);
        Assert.True(result.IsSuccess, result.IsFailure ? result.Error.Message : null);
        return result.Value;
    }

    /// <summary>Serialises the way MailKit does on the wire — prepared for the server's 7-bit or 8-bit constraint,
    /// CRLF line breaks — and parses the bytes back.</summary>
    private static (MimeMessage Parsed, string Raw) RoundTrip(MimeMessage message, EncodingConstraint constraint = EncodingConstraint.SevenBit)
    {
        message.Prepare(constraint);

        var options = FormatOptions.Default.Clone();
        options.NewLineFormat = NewLineFormat.Dos;

        using var stream = new MemoryStream();
        message.WriteTo(options, stream);

        var bytes = stream.ToArray();
        using var parseStream = new MemoryStream(bytes);
        return (MimeMessage.Load(parseStream), Encoding.Latin1.GetString(bytes));
    }

    private static string[] HeaderLines(string raw) =>
        raw[..raw.IndexOf("\r\n\r\n", StringComparison.Ordinal)].Split("\r\n");

    // ---- no calendar: unchanged behaviour -------------------------------------------------------------------

    [Fact]
    public void Create_WithoutCalendar_BuildsTheSameHtmlOnlyBodyAsBodyBuilder()
    {
        using var mime = Build(Plain());
        using var expected = new MimeMessage();
        expected.Body = new BodyBuilder { HtmlBody = Html }.ToMessageBody();

        var body = Assert.IsType<TextPart>(mime.Body);
        Assert.True(body.IsHtml);
        Assert.Equal("utf-8", body.ContentType.Charset);
        Assert.Equal(Html, body.Text);
        Assert.Equal(expected.Body.ToString(), mime.Body.ToString());
    }

    [Fact]
    public void Create_WithoutCalendar_SetsFromToAndSubject()
    {
        using var mime = Build(Plain(subject: "ใบเสร็จรับเงิน"));

        var from = Assert.IsType<MailboxAddress>(Assert.Single(mime.From));
        Assert.Equal(From, from.Address);
        Assert.Equal(FromName, from.Name);
        var to = Assert.IsType<MailboxAddress>(Assert.Single(mime.To));
        Assert.Equal(To, to.Address);
        Assert.Equal("ใบเสร็จรับเงิน", mime.Subject);
        Assert.Empty(mime.Cc);
        Assert.Empty(mime.Bcc);
    }

    [Fact]
    public void Create_RecipientWithDisplayName_IsStillAccepted()
    {
        using var mime = Build(Plain(toAddress: "สมชาย ใจดี <somchai@example.test>"));

        var to = Assert.IsType<MailboxAddress>(Assert.Single(mime.To));
        Assert.Equal("somchai@example.test", to.Address);
    }

    // ---- with calendar: structure ---------------------------------------------------------------------------

    [Theory]
    [InlineData("REQUEST", EncodingConstraint.SevenBit)]
    [InlineData("CANCEL", EncodingConstraint.SevenBit)]
    [InlineData("PUBLISH", EncodingConstraint.SevenBit)]
    [InlineData("REQUEST", EncodingConstraint.EightBit)]
    [InlineData("CANCEL", EncodingConstraint.EightBit)]
    [InlineData("PUBLISH", EncodingConstraint.EightBit)]
    public void Create_WithCalendar_BuildsMixedWithHtmlAndCalendarAlternativeThenAttachment(string method, EncodingConstraint constraint)
    {
        var ics = SampleIcs(method);
        using var mime = Build(WithCalendar(method, ics));

        var (parsed, _) = RoundTrip(mime, constraint);

        var mixed = Assert.IsAssignableFrom<Multipart>(parsed.Body);
        Assert.True(mixed.ContentType.IsMimeType("multipart", "mixed"));
        Assert.Equal(2, mixed.Count);

        var alternative = Assert.IsAssignableFrom<Multipart>(mixed[0]);
        Assert.True(alternative.ContentType.IsMimeType("multipart", "alternative"));
        Assert.Equal(2, alternative.Count);

        var html = Assert.IsType<TextPart>(alternative[0]);
        Assert.True(html.ContentType.IsMimeType("text", "html"));
        Assert.Equal("utf-8", html.ContentType.Charset);
        Assert.Equal(Html, html.Text);

        var inline = Assert.IsType<TextPart>(alternative[1]);
        Assert.True(inline.ContentType.IsMimeType("text", "calendar"));
        Assert.Equal(method, inline.ContentType.Parameters["method"]);
        Assert.Equal("utf-8", inline.ContentType.Charset);
        Assert.Equal(ics, inline.Text);
        Assert.False(inline.IsAttachment);

        var attachment = Assert.IsType<TextPart>(mixed[1]);
        Assert.True(attachment.ContentType.IsMimeType("text", "calendar"));
        Assert.Equal(method, attachment.ContentType.Parameters["method"]);
        Assert.Equal("utf-8", attachment.ContentType.Charset);
        Assert.True(attachment.IsAttachment);
        Assert.Equal("attachment", attachment.ContentDisposition?.Disposition);
        Assert.Equal("invite.ics", attachment.ContentDisposition?.FileName);
        Assert.Equal("invite.ics", attachment.ContentType.Name);
        Assert.Equal(ics, attachment.Text);
    }

    [Theory]
    [InlineData("REQUEST")]
    [InlineData("CANCEL")]
    [InlineData("PUBLISH")]
    public void Create_WithCalendar_WritesTheMethodParameterIntoTheRawContentTypeHeader(string method)
    {
        using var mime = Build(WithCalendar(method));

        var (_, raw) = RoundTrip(mime);

        // Both the inline alternative and the attachment carry it.
        var contentTypeLines = raw.Split("\r\n").Where(l => l.StartsWith("Content-Type: text/calendar", StringComparison.Ordinal)).ToList();
        Assert.Equal(2, contentTypeLines.Count);
        Assert.All(contentTypeLines, l => Assert.Contains($"method={method}", l, StringComparison.Ordinal));
        Assert.All(contentTypeLines, l => Assert.Contains("charset=utf-8", l, StringComparison.Ordinal));
        Assert.Contains("name=invite.ics", raw, StringComparison.Ordinal);
        Assert.Contains("filename=invite.ics", raw, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_WithCalendar_KeepsThaiHtmlAndCalendarTextIntactOverASevenBitRelay()
    {
        using var mime = Build(WithCalendar());

        var (parsed, raw) = RoundTrip(mime, EncodingConstraint.SevenBit);

        // Nothing 8-bit on the wire: every byte is ASCII, yet the parsed-back text is the original Thai.
        Assert.All(Encoding.Latin1.GetBytes(raw), b => Assert.True(b < 0x80, "message must be 7-bit clean"));
        var mixed = Assert.IsAssignableFrom<Multipart>(parsed.Body);
        var alternative = Assert.IsAssignableFrom<Multipart>(mixed[0]);
        Assert.Contains("ยืนยันตารางเรียนสด", ((TextPart)alternative[0]).Text, StringComparison.Ordinal);
        Assert.Contains("การเขียนโปรแกรมเชิงวัตถุ", ((TextPart)alternative[1]).Text, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_WithCalendar_KeepsTheTopLevelHeadersOfAnOrdinaryEmail()
    {
        using var mime = Build(WithCalendar(subject: "ยืนยันตารางเรียนสด: คอร์ส Python"));

        var (parsed, _) = RoundTrip(mime);

        Assert.Equal(From, Assert.IsType<MailboxAddress>(Assert.Single(parsed.From)).Address);
        Assert.Equal(To, Assert.IsType<MailboxAddress>(Assert.Single(parsed.To)).Address);
        Assert.Equal("ยืนยันตารางเรียนสด: คอร์ส Python", parsed.Subject);
        Assert.Empty(parsed.Cc);
        Assert.Empty(parsed.Bcc);
    }

    [Fact]
    public void Create_WithCalendar_NormalisesBareLineFeedsToCrLf()
    {
        var ics = SampleIcs().Replace("\r\n", "\n", StringComparison.Ordinal);
        using var mime = Build(WithCalendar(ics: ics));

        var (parsed, _) = RoundTrip(mime);

        var mixed = Assert.IsAssignableFrom<Multipart>(parsed.Body);
        var attachment = Assert.IsType<TextPart>(mixed[1]);
        Assert.Equal(SampleIcs(), attachment.Text);
        Assert.DoesNotContain("\n", attachment.Text.Replace("\r\n", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public void Create_WithCalendar_IcsContainingMimeLookalikeText_StaysInsideItsPart()
    {
        // A value the ICS builder failed to neutralise must still be only body text: it cannot add a header
        // or a sibling MIME part to the message.
        var ics = SampleIcs().Replace(
            "STATUS:CONFIRMED",
            "DESCRIPTION:x\r\n\r\nContent-Type: text/html\r\n\r\n<script>alert(1)</script>\r\nBcc: attacker@evil.test\r\nSTATUS:CONFIRMED",
            StringComparison.Ordinal);
        using var mime = Build(WithCalendar(ics: ics));

        var (parsed, raw) = RoundTrip(mime);

        var mixed = Assert.IsAssignableFrom<Multipart>(parsed.Body);
        Assert.Equal(2, mixed.Count);
        Assert.Equal(2, Assert.IsAssignableFrom<Multipart>(mixed[0]).Count);
        Assert.Empty(parsed.Bcc);
        Assert.DoesNotContain(HeaderLines(raw), l => l.StartsWith("Bcc:", StringComparison.OrdinalIgnoreCase));
        Assert.Equal(ics, ((TextPart)mixed[1]).Text);
    }

    // ---- calendar: validation -------------------------------------------------------------------------------

    [Theory]
    [InlineData("")]
    [InlineData("request")]
    [InlineData("Request")]
    [InlineData("REPLY")]
    [InlineData("COUNTER")]
    [InlineData("REQUEST ")]
    [InlineData("REQUEST\r\nBcc: attacker@evil.test")]
    [InlineData("REQUEST; charset=utf-7")]
    [InlineData("REQUEST\"")]
    public void Create_UnknownOrInjectedCalendarMethod_FailsWithoutBuildingAMessage(string method)
    {
        var result = EmailMimeMessageFactory.Create(WithCalendar(method, SampleIcs()), From, FromName);

        Assert.True(result.IsFailure);
        Assert.Equal(EmailMimeMessageFactory.InvalidCalendarCode, result.Error.Code);
    }

    [Fact]
    public void Create_NullCalendarMethod_Fails()
    {
        var message = new EmailMessage(To, "s", Html, new EmailCalendarContent(null!, SampleIcs()));

        var result = EmailMimeMessageFactory.Create(message, From, FromName);

        Assert.True(result.IsFailure);
        Assert.Equal(EmailMimeMessageFactory.InvalidCalendarCode, result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("VERSION:2.0\r\nBEGIN:VCALENDAR\r\nEND:VCALENDAR")]
    [InlineData(" BEGIN:VCALENDAR\r\nEND:VCALENDAR")]
    [InlineData("begin:vcalendar\r\nEND:VCALENDAR")]
    public void Create_CalendarContentThatIsNotAnIcsDocument_Fails(string ics)
    {
        var result = EmailMimeMessageFactory.Create(WithCalendar(ics: ics), From, FromName);

        Assert.True(result.IsFailure);
        Assert.Equal(EmailMimeMessageFactory.InvalidCalendarCode, result.Error.Code);
    }

    [Fact]
    public void Create_NullCalendarContent_Fails()
    {
        var message = new EmailMessage(To, "s", Html, new EmailCalendarContent("REQUEST", null!));

        var result = EmailMimeMessageFactory.Create(message, From, FromName);

        Assert.True(result.IsFailure);
        Assert.Equal(EmailMimeMessageFactory.InvalidCalendarCode, result.Error.Code);
    }

    [Fact]
    public void Create_CalendarContentAtTheSizeCap_IsAcceptedAndOneCharacterOverIsRejected()
    {
        const string head = "BEGIN:VCALENDAR\r\nDESCRIPTION:";
        const string tail = "\r\nEND:VCALENDAR\r\n";
        var atCap = head + new string('a', EmailMimeMessageFactory.MaxCalendarContentLength - head.Length - tail.Length) + tail;
        Assert.Equal(EmailMimeMessageFactory.MaxCalendarContentLength, atCap.Length);

        using var accepted = Build(WithCalendar(ics: atCap));
        var (parsed, _) = RoundTrip(accepted);
        Assert.Equal(2, Assert.IsAssignableFrom<Multipart>(parsed.Body).Count);

        var tooBig = EmailMimeMessageFactory.Create(WithCalendar(ics: atCap + "x"), From, FromName);
        Assert.True(tooBig.IsFailure);
        Assert.Equal(EmailMimeMessageFactory.InvalidCalendarCode, tooBig.Error.Code);
    }

    // ---- recipient / subject injection ----------------------------------------------------------------------

    [Theory]
    [InlineData("learner@example.test\r\nBcc: attacker@evil.test")]
    [InlineData("learner@example.test\nBcc: attacker@evil.test")]
    [InlineData("learner@example.test\rBcc: attacker@evil.test")]
    [InlineData("learner@example.test, attacker@evil.test")]
    [InlineData("learner@example.test; attacker@evil.test")]
    [InlineData("<learner@example.test> <attacker@evil.test>")]
    [InlineData("undisclosed: learner@example.test, attacker@evil.test;")]
    [InlineData("learner@example.test\0")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not-an-address")]
    public void Create_RecipientThatIsNotExactlyOneCleanMailbox_FailsWithoutBuildingAMessage(string toAddress)
    {
        var plain = EmailMimeMessageFactory.Create(Plain(toAddress), From, FromName);
        var withCalendar = EmailMimeMessageFactory.Create(WithCalendar(toAddress: toAddress), From, FromName);

        Assert.True(plain.IsFailure);
        Assert.Equal(EmailMimeMessageFactory.InvalidRecipientCode, plain.Error.Code);
        Assert.True(withCalendar.IsFailure);
        Assert.Equal(EmailMimeMessageFactory.InvalidRecipientCode, withCalendar.Error.Code);
    }

    [Theory]
    [InlineData("Hello\r\nBcc: attacker@evil.test")]
    [InlineData("Hello\nBcc: attacker@evil.test")]
    [InlineData("Hello\rBcc: attacker@evil.test")]
    [InlineData("Hello\r\nX-Evil: 1\r\n\r\n<script>alert(1)</script>")]
    [InlineData("Hello\u0085Bcc: attacker@evil.test")]
    [InlineData("Hello\u2028Bcc: attacker@evil.test")]
    [InlineData("Hello\u2029Bcc: attacker@evil.test")]
    [InlineData("Hello\0Bcc: attacker@evil.test")]
    public void Create_SubjectWithLineBreaks_CannotInjectHeadersOrBodyOnEitherPath(string subject)
    {
        foreach (var message in new[] { Plain(subject: subject), WithCalendar(subject: subject) })
        {
            using var mime = Build(message);

            var (parsed, raw) = RoundTrip(mime);

            Assert.Empty(parsed.Bcc);
            Assert.False(parsed.Headers.Contains("X-Evil"));
            Assert.DoesNotContain(HeaderLines(raw), l => l.StartsWith("Bcc:", StringComparison.OrdinalIgnoreCase));
            Assert.DoesNotContain(HeaderLines(raw), l => l.StartsWith("X-Evil:", StringComparison.OrdinalIgnoreCase));
            Assert.StartsWith("Hello ", parsed.Subject, StringComparison.Ordinal);
            Assert.DoesNotContain((parsed.Subject ?? string.Empty).ToCharArray(), c => char.IsControl(c) || char.GetUnicodeCategory(c) is System.Globalization.UnicodeCategory.LineSeparator or System.Globalization.UnicodeCategory.ParagraphSeparator);
        }
    }

    [Fact]
    public void Create_OrdinarySubject_IsPassedThroughUntouched()
    {
        using var mime = Build(Plain(subject: "ยืนยันตารางเรียนสด: คอร์ส \"Python\" & Data  (รุ่น 2)"));

        Assert.Equal("ยืนยันตารางเรียนสด: คอร์ส \"Python\" & Data  (รุ่น 2)", mime.Subject);
    }

    [Fact]
    public void Create_VeryLongThaiSubject_IsFoldedIntoEncodedWordsWithoutBreakingTheHeaderBlock()
    {
        var subject = string.Concat(Enumerable.Repeat("ยกเลิกคาบเรียนสด ", 30));
        using var mime = Build(WithCalendar(subject: subject));

        var (parsed, raw) = RoundTrip(mime);

        Assert.Equal(subject.Trim(), parsed.Subject?.Trim());
        Assert.All(HeaderLines(raw), l => Assert.True(l.Length <= 998));
    }
}
