using System.Text;
using System.Text.RegularExpressions;
using Siri.Modules.Live.Application;

namespace Siri.UnitTests.Live;

/// <summary>Unit tests for <see cref="IcsCalendarBuilder"/> (docs/contracts/P11-04-live-invites-ics-reminders.md §4.1): every rule is
/// asserted on the unfolded document, so the tests read the same text a calendar client would.</summary>
public class IcsCalendarBuilderTests
{
    private const string JoinBase = "https://app.example.test/live";
    private const string UidHost = "app.example.test";
    private static readonly DateTime Now = new(2026, 10, 7, 3, 0, 0, DateTimeKind.Utc);

    private static IcsEvent Event(
        Guid? sessionId = null,
        int sequence = 0,
        string summary = "คอร์สทดสอบ — คาบที่ 1",
        string? description = null,
        bool cancelled = false,
        DateTime? start = null)
    {
        var id = sessionId ?? Guid.NewGuid();
        var starts = start ?? new DateTime(2026, 10, 8, 3, 0, 0, DateTimeKind.Utc);
        return new IcsEvent(id, sequence, summary, description, starts, starts.AddHours(2), $"{JoinBase}/{id:D}/join", cancelled);
    }

    private static string Build(
        IcsMethod method,
        IReadOnlyList<IcsEvent> events,
        string? attendeeEmail = null,
        string? attendeeName = null,
        string organizerName = "SIRI UpSkill") =>
        IcsCalendarBuilder.Build(method, events, "no-reply@app.example.test", organizerName, UidHost, attendeeEmail, attendeeName, Now);

    private static IReadOnlyList<string> Lines(string ics) => IcsText.UnfoldedLines(ics);

    // ---- Document shape ----------------------------------------------------------------------------

    [Theory]
    [InlineData(IcsMethod.Publish, "PUBLISH")]
    [InlineData(IcsMethod.Request, "REQUEST")]
    [InlineData(IcsMethod.Cancel, "CANCEL")]
    public void Build_WritesTheCalendarHeader_AndAMethodLineEqualToTheMethod(IcsMethod method, string token)
    {
        var ics = Build(method, [Event()], "learner@example.test", "ผู้เรียน");

        var lines = Lines(ics);
        Assert.StartsWith("BEGIN:VCALENDAR", ics, StringComparison.Ordinal);
        Assert.Equal("BEGIN:VCALENDAR", lines[0]);
        Assert.Equal("VERSION:2.0", lines[1]);
        Assert.Equal("PRODID:-//SIRI UpSkill//Live Sessions//TH", lines[2]);
        Assert.Equal("CALSCALE:GREGORIAN", lines[3]);
        Assert.Equal($"METHOD:{token}", lines[4]);
        Assert.Equal("X-WR-TIMEZONE:Asia/Bangkok", lines[5]);
        Assert.Equal("END:VCALENDAR", lines[^1]);
        Assert.Equal(token, IcsCalendarBuilder.MethodToken(method));
        Assert.True(ics.Length <= 200_000);
    }

    [Fact]
    public void Build_EveryLineEndsWithCrlf_AndNoBareLineFeedExists()
    {
        var ics = Build(IcsMethod.Publish, [Event(), Event()]);

        Assert.EndsWith("\r\n", ics, StringComparison.Ordinal);
        Assert.DoesNotContain("\n", ics.Replace("\r\n", string.Empty, StringComparison.Ordinal));
        Assert.DoesNotContain("\r", ics.Replace("\r\n", string.Empty, StringComparison.Ordinal));
    }

    [Fact]
    public void Build_Publish_HasNoAttendee_EvenIfAnAddressIsPassed()
    {
        var ics = Build(IcsMethod.Publish, [Event(), Event()], "learner@example.test", "ผู้เรียน");

        Assert.DoesNotContain(Lines(ics), l => l.StartsWith("ATTENDEE", StringComparison.Ordinal));
        Assert.DoesNotContain("learner@example.test", ics, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(IcsMethod.Request)]
    [InlineData(IcsMethod.Cancel)]
    public void Build_RequestAndCancel_CarryExactlyOneAttendeeWithTheRsvpParameters(IcsMethod method)
    {
        var ics = Build(method, [Event()], "learner@example.test", "ผู้เรียน ทดสอบ");

        var attendee = Assert.Single(Lines(ics), l => l.StartsWith("ATTENDEE", StringComparison.Ordinal));
        Assert.Equal(
            "ATTENDEE;CN=\"ผู้เรียน ทดสอบ\";ROLE=REQ-PARTICIPANT;PARTSTAT=NEEDS-ACTION;RSVP=FALSE:mailto:learner@example.test",
            attendee);
    }

    [Fact]
    public void Build_Event_HasTheContractProperties()
    {
        var sessionId = Guid.NewGuid();
        var ics = Build(
            IcsMethod.Request,
            [Event(sessionId, sequence: 3, summary: "คอร์ส — คาบ", description: "รายละเอียดคาบ")],
            "learner@example.test",
            "ผู้เรียน");

        var lines = Lines(ics);
        Assert.Contains($"UID:{sessionId:N}@{UidHost}", lines);
        Assert.Contains("DTSTAMP:20261007T030000Z", lines);
        Assert.Contains("SEQUENCE:3", lines);
        Assert.Contains("DTSTART:20261008T030000Z", lines);
        Assert.Contains("DTEND:20261008T050000Z", lines);
        Assert.Contains("SUMMARY:คอร์ส — คาบ", lines);
        Assert.Contains($"LOCATION:ออนไลน์ — {JoinBase}/{sessionId:D}/join", lines);
        Assert.Contains($"URL:{JoinBase}/{sessionId:D}/join", lines);
        Assert.Contains("ORGANIZER;CN=\"SIRI UpSkill\":mailto:no-reply@app.example.test", lines);
        Assert.Contains("TRANSP:OPAQUE", lines);
        Assert.Contains("STATUS:CONFIRMED", lines);

        var description = Assert.Single(lines, l => l.StartsWith("DESCRIPTION:เข้าห้องผ่านแพลตฟอร์ม", StringComparison.Ordinal));
        Assert.Contains("ก่อนเวลา 15 นาที", description, StringComparison.Ordinal);
        Assert.Contains($"{JoinBase}/{sessionId:D}/join", description, StringComparison.Ordinal);
        Assert.Contains("\\n\\nรายละเอียดคาบ", description, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_Event_UsesUtcTimes_NeverATzidOrVtimezone()
    {
        var ics = Build(IcsMethod.Publish, [Event()]);

        Assert.DoesNotContain("TZID", ics, StringComparison.Ordinal);
        Assert.DoesNotContain("VTIMEZONE", ics, StringComparison.Ordinal);
        Assert.Matches(new Regex(@"^DTSTART:\d{8}T\d{6}Z$", RegexOptions.Multiline), string.Join("\n", Lines(ics)));
    }

    [Fact]
    public void Build_NonUtcAndUnspecifiedKinds_AreNormalisedToUtc()
    {
        var local = new DateTime(2026, 10, 8, 10, 0, 0, DateTimeKind.Unspecified); // the database hands back unspecified/UTC values
        var ics = Build(IcsMethod.Publish, [Event(start: local)]);

        Assert.Contains("DTSTART:20261008T100000Z", Lines(ics));
    }

    [Fact]
    public void Build_ConfirmedEvent_HasA15MinuteDisplayAlarm_ButACancelledOneHasNone()
    {
        var confirmed = Lines(Build(IcsMethod.Publish, [Event()]));
        Assert.Contains("BEGIN:VALARM", confirmed);
        Assert.Contains("TRIGGER:-PT15M", confirmed);
        Assert.Contains("ACTION:DISPLAY", confirmed);

        var cancelled = Lines(Build(IcsMethod.Cancel, [Event(cancelled: true)], "learner@example.test", "ผู้เรียน"));
        Assert.DoesNotContain("BEGIN:VALARM", cancelled);
        Assert.Contains("STATUS:CANCELLED", cancelled);
    }

    [Fact]
    public void Build_CancelledFlag_WritesStatusCancelled_EvenForARequest()
    {
        var lines = Lines(Build(IcsMethod.Request, [Event(cancelled: true)], "learner@example.test", "ผู้เรียน"));

        Assert.Contains("STATUS:CANCELLED", lines);
        Assert.DoesNotContain("STATUS:CONFIRMED", lines);
    }

    [Fact]
    public void Build_CancelMethod_ForcesStatusCancelled_EvenWhenTheEventIsNotFlagged()
    {
        var lines = Lines(Build(IcsMethod.Cancel, [Event(cancelled: false)], "learner@example.test", "ผู้เรียน"));

        Assert.Contains("STATUS:CANCELLED", lines);
    }

    [Fact]
    public void Build_SeveralEvents_EmitsOneVeventEach_WithDistinctStableUids()
    {
        var ids = Enumerable.Range(0, 5).Select(_ => Guid.NewGuid()).ToArray();

        var ics = Build(IcsMethod.Publish, ids.Select(id => Event(id, sequence: 2)).ToList());

        var lines = Lines(ics);
        Assert.Equal(5, lines.Count(l => l == "BEGIN:VEVENT"));
        Assert.Equal(5, lines.Count(l => l == "END:VEVENT"));
        Assert.Equal(ids.Select(id => $"UID:{id:N}@{UidHost}").Order(), lines.Where(l => l.StartsWith("UID:", StringComparison.Ordinal)).Order());
    }

    [Fact]
    public void Build_TheUidOfASessionIsTheSame_AcrossPublishRequestAndCancel()
    {
        var sessionId = Guid.NewGuid();
        var uid = $"UID:{sessionId:N}@{UidHost}";

        Assert.Contains(uid, Lines(Build(IcsMethod.Publish, [Event(sessionId)])));
        Assert.Contains(uid, Lines(Build(IcsMethod.Request, [Event(sessionId, 1)], "a@example.test", "A")));
        Assert.Contains(uid, Lines(Build(IcsMethod.Cancel, [Event(sessionId, 2, cancelled: true)], "a@example.test", "A")));
        Assert.Equal(IcsCalendarBuilder.BuildUid(sessionId, UidHost), uid["UID:".Length..]);
    }

    [Fact]
    public void Build_FiftyEventsWithLongThaiDescriptions_StaysUnderTheOutboxLimit()
    {
        var longDescription = string.Concat(Enumerable.Repeat("คำอธิบายภาษาไทยที่ยาวมาก ", 200));

        var ics = Build(IcsMethod.Publish, Enumerable.Range(0, 50).Select(i => Event(description: longDescription)).ToList());

        Assert.True(ics.Length <= 200_000, $"document is {ics.Length} characters");
        Assert.Equal(50, Lines(ics).Count(l => l == "BEGIN:VEVENT"));
    }

    [Fact]
    public void Build_DescriptionLongerThanTheCap_IsTruncated()
    {
        var ics = Build(IcsMethod.Publish, [Event(description: new string('ฆ', 5000))]);

        var description = Assert.Single(Lines(ics), l => l.StartsWith("DESCRIPTION:เข้าห้อง", StringComparison.Ordinal));
        Assert.True(description.Count(c => c == 'ฆ') <= IcsCalendarBuilder.MaxDescriptionCharacters);
    }

    // ---- Folding ---------------------------------------------------------------------------------------

    [Fact]
    public void Build_NoPhysicalLineExceeds75Octets_AndContinuationsStartWithASpace()
    {
        var ics = Build(
            IcsMethod.Request,
            [Event(summary: string.Concat(Enumerable.Repeat("ชื่อคอร์สภาษาไทยที่ยาวมากเกินเจ็ดสิบห้าไบต์ ", 8)), description: new string('ข', 600))],
            "learner@example.test",
            "ผู้เรียนที่มีชื่อยาวมากมายเหลือเกิน");

        foreach (var physical in ics.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            Assert.True(Encoding.UTF8.GetByteCount(physical) <= IcsCalendarBuilder.MaxLineOctets, $"line is {Encoding.UTF8.GetByteCount(physical)} octets: {physical}");
        }

        Assert.Contains("\r\n ", ics, StringComparison.Ordinal);
    }

    [Fact]
    public void Build_FoldingNeverSplitsACharacter_AndUnfoldingRestoresTheText()
    {
        var summary = string.Concat(Enumerable.Repeat("สวัสดีชาวโลก 🙂 ", 20)); // Thai (3 octets) and an emoji (4 octets) around the fold points
        var ics = Build(IcsMethod.Publish, [Event(summary: summary)]);

        var strict = new UTF8Encoding(false, throwOnInvalidBytes: true);
        foreach (var physical in ics.Split("\r\n", StringSplitOptions.RemoveEmptyEntries))
        {
            // each physical line must be valid UTF-8 on its own — a split inside a multi-octet character would not be
            strict.GetString(strict.GetBytes(physical));
            Assert.DoesNotContain('�', physical);
        }

        var unfolded = Lines(ics).Single(l => l.StartsWith("SUMMARY:", StringComparison.Ordinal));
        Assert.Equal("SUMMARY:" + summary, unfolded);
    }

    [Fact]
    public void AppendFolded_ALineOfExactly75Octets_IsNotFolded_And76IsFolded()
    {
        var exact = new StringBuilder();
        IcsCalendarBuilder.AppendFolded(exact, new string('a', 75));
        Assert.Equal(new string('a', 75) + "\r\n", exact.ToString());

        var over = new StringBuilder();
        IcsCalendarBuilder.AppendFolded(over, new string('a', 76));
        Assert.Equal(new string('a', 75) + "\r\n a\r\n", over.ToString());
    }

    // ---- Escaping and injection ---------------------------------------------------------------------

    [Theory]
    [InlineData("a,b", "a\\,b")]
    [InlineData("a;b", "a\\;b")]
    [InlineData("a\\b", "a\\\\b")]
    [InlineData("line1\nline2", "line1\\nline2")]
    [InlineData("line1\r\nline2", "line1\\nline2")]
    [InlineData("line1\rline2", "line1\\nline2")]
    [InlineData("tab\there", "tab here")]
    public void EscapeText_FollowsRfc5545(string input, string expected)
    {
        Assert.Equal(expected, IcsCalendarBuilder.EscapeText(input));
    }

    [Fact]
    public void EscapeText_DropsOtherControlCharacters()
    {
        Assert.Equal("ab", IcsCalendarBuilder.EscapeText("a\u0000\u0007b"));
        Assert.Equal(string.Empty, IcsCalendarBuilder.EscapeText(null));
    }

    [Fact]
    public void Build_SummaryInjection_CannotCreateANewContentLine()
    {
        const string malicious = "คอร์ส\r\nATTENDEE;ROLE=CHAIR:mailto:attacker@example.test\r\nEND:VEVENT\r\nBEGIN:VEVENT";

        var publish = Build(IcsMethod.Publish, [Event(summary: malicious, description: malicious)]);
        var request = Build(IcsMethod.Request, [Event(summary: malicious, description: malicious)], "learner@example.test", "ผู้เรียน");

        Assert.DoesNotContain(Lines(publish), l => l.StartsWith("ATTENDEE", StringComparison.Ordinal));
        Assert.Equal(1, Lines(publish).Count(l => l == "BEGIN:VEVENT"));
        Assert.Equal(1, Lines(publish).Count(l => l == "END:VEVENT"));

        var attendee = Assert.Single(Lines(request), l => l.StartsWith("ATTENDEE", StringComparison.Ordinal));
        Assert.Contains("learner@example.test", attendee, StringComparison.Ordinal);
        Assert.Equal(1, Lines(request).Count(l => l == "BEGIN:VEVENT"));
    }

    [Fact]
    public void Build_CommonNameInjection_StaysInsideTheQuotedParameter()
    {
        var ics = Build(IcsMethod.Request, [Event()], "learner@example.test", "Bob\";ROLE=CHAIR:mailto:evil@example.test\r\nX-EVIL:1");

        var attendee = Assert.Single(Lines(ics), l => l.StartsWith("ATTENDEE", StringComparison.Ordinal));
        Assert.StartsWith("ATTENDEE;CN=\"Bob';ROLE=CHAIR:mailto:evil@example.test", attendee, StringComparison.Ordinal);
        Assert.EndsWith(":mailto:learner@example.test", attendee, StringComparison.Ordinal);
        Assert.DoesNotContain(Lines(ics), l => l.StartsWith("X-EVIL", StringComparison.Ordinal));
    }

    [Fact]
    public void Build_OrganizerNameInjection_IsNeutralised()
    {
        var ics = Build(IcsMethod.Publish, [Event()], organizerName: "SIRI\r\nATTENDEE:mailto:evil@example.test");

        Assert.DoesNotContain(Lines(ics), l => l.StartsWith("ATTENDEE", StringComparison.Ordinal));
    }

    // ---- Only the platform join URL ------------------------------------------------------------------

    [Fact]
    public void Build_TheOnlyUrlsInTheDocument_AreThePlatformJoinUrl()
    {
        var sessionId = Guid.NewGuid();

        var ics = Build(IcsMethod.Request, [Event(sessionId, description: "ดูรายละเอียดที่แพลตฟอร์ม")], "learner@example.test", "ผู้เรียน");

        var joinUrl = $"{JoinBase}/{sessionId:D}/join";
        var urls = Regex.Matches(string.Join("\n", Lines(ics)), @"https?://[^\s\\]+").Select(m => m.Value).ToList();
        Assert.NotEmpty(urls);
        Assert.All(urls, url => Assert.Equal(joinUrl, url));
    }

    // ---- Input validation -----------------------------------------------------------------------------

    [Fact]
    public void Build_NoEvents_Throws()
    {
        Assert.Throws<ArgumentException>(() => Build(IcsMethod.Publish, []));
    }

    [Theory]
    [InlineData("")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/live/abc/join")]
    [InlineData("https://app.example.test/join with space")]
    public void Build_AJoinUrlThatIsNotAnAbsoluteWebUrl_Throws(string joinUrl)
    {
        var bad = Event() with { JoinUrl = joinUrl };

        Assert.Throws<ArgumentException>(() => Build(IcsMethod.Publish, [bad]));
    }

    [Theory]
    [InlineData(IcsMethod.Request)]
    [InlineData(IcsMethod.Cancel)]
    public void Build_RequestOrCancelWithoutAnAttendee_Throws(IcsMethod method)
    {
        Assert.Throws<ArgumentException>(() => Build(method, [Event()]));
    }

    [Theory]
    [InlineData("learner@example.test\r\nBCC:evil@example.test")]
    [InlineData("a@b.test, c@d.test")]
    [InlineData("no-at-sign")]
    [InlineData("<a@b.test>")]
    public void Build_AMalformedAttendeeAddress_Throws(string email)
    {
        Assert.Throws<ArgumentException>(() => Build(IcsMethod.Request, [Event()], email, "ผู้เรียน"));
    }

    [Fact]
    public void Build_NegativeSequence_Throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Build(IcsMethod.Publish, [Event(sequence: -1)]));
    }
}
