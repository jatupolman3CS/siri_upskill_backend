using System.Net;
using System.Text.RegularExpressions;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Live.Application;

namespace Siri.UnitTests.Live;

/// <summary>Unit tests for <see cref="LiveEmailTemplates"/> (docs/contracts/P11-04-live-invites-ics-reminders.md §4.3): Thai text, HTML
/// encoding of every user/database value, single-line subjects, and platform links only.</summary>
public class LiveEmailTemplatesTests
{
    private const string Base = "https://app.example.test";

    private static readonly LiveTemplateSettings Settings = new(Base, 15, LiveOptions.DefaultAllowedMeetingHosts);

    private static LiveSessionContext Session(
        string title = "คาบที่ 1",
        string? description = null,
        string? cancelReason = null,
        Guid? id = null,
        DateTime? start = null) =>
        LiveTestData.Context(
            sessionId: id,
            title: title,
            description: description,
            startsAtUtc: start ?? new DateTime(2026, 10, 8, 3, 0, 0, DateTimeKind.Utc),
            endsAtUtc: (start ?? new DateTime(2026, 10, 8, 3, 0, 0, DateTimeKind.Utc)).AddHours(2)) with
        {
            CancelReason = cancelReason,
        };

    private static IReadOnlyList<string> Hrefs(string html) =>
        Regex.Matches(html, "href=\"([^\"]*)\"").Select(m => WebUtility.HtmlDecode(m.Groups[1].Value)).ToList();

    private static void AssertSafe(LiveMessage message)
    {
        Assert.DoesNotContain('\r', message.Subject);
        Assert.DoesNotContain('\n', message.Subject);
        Assert.False(InviteHarness.ContainsMeetingHost(message.Subject + message.BodyHtml + message.InApp.Title + message.InApp.Body));
        Assert.All(Hrefs(message.BodyHtml), href => Assert.StartsWith(Base + "/", href, StringComparison.Ordinal));
        Assert.StartsWith("/", message.InApp.LinkPath, StringComparison.Ordinal);
        Assert.DoesNotContain("<script", message.BodyHtml, StringComparison.OrdinalIgnoreCase);
    }

    // ---- Learner batch --------------------------------------------------------------------------------

    [Fact]
    public void LearnerInviteBatch_FirstTime_UsesTheConfirmSubject_AndListsEverySessionWithItsJoinLink()
    {
        var sessions = Enumerable.Range(0, 3).Select(i => Session($"คาบที่ {i + 1}", start: new DateTime(2026, 10, 8 + i, 3, 0, 0, DateTimeKind.Utc))).ToList();

        var message = LiveEmailTemplates.LearnerInviteBatch(Settings, "คอร์สภาษาไทย", "thai-course", sessions, isFirst: true);

        Assert.Equal("ยืนยันตารางเรียนสด: คอร์สภาษาไทย", message.Subject);
        Assert.Equal("live-invite-batch", message.TemplateKey);
        foreach (var session in sessions)
        {
            Assert.Contains($"{Base}/live/{session.SessionId:D}/join", Hrefs(message.BodyHtml));
            Assert.Contains(ThaiDateText.Format(session.StartsAtUtc, session.EndsAtUtc), message.BodyHtml, StringComparison.Ordinal);
        }

        Assert.Contains("ลิงก์เข้าห้องเรียนใช้ได้เฉพาะบัญชีของคุณ — โปรดอย่าส่งต่อ", message.BodyHtml, StringComparison.Ordinal);
        Assert.Equal("live.invite", message.InApp.Type);
        Assert.Equal("/learn/thai-course?tab=live", message.InApp.LinkPath);
        AssertSafe(message);
    }

    [Fact]
    public void LearnerInviteBatch_NotFirstTime_UsesTheAddedSessionsSubject()
    {
        var message = LiveEmailTemplates.LearnerInviteBatch(Settings, "คอร์ส", "c", [Session()], isFirst: false);

        Assert.Equal("เพิ่มคาบเรียนสดใหม่: คอร์ส", message.Subject);
    }

    [Fact]
    public void LearnerInviteBatch_MoreThan30Sessions_ShowsThirtyRowsAndAnAndMoreLine()
    {
        var sessions = Enumerable.Range(0, 45).Select(i => Session($"คาบ {i}")).ToList();

        var message = LiveEmailTemplates.LearnerInviteBatch(Settings, "คอร์ส", "c", sessions, isFirst: true);

        Assert.Equal(30, Regex.Matches(message.BodyHtml, "เข้าห้องเรียน</a>").Count);
        Assert.Contains("และอีก 15 คาบ", message.BodyHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void LearnerInviteBatch_SlugWithSpecialCharacters_IsEscapedInThePath()
    {
        var message = LiveEmailTemplates.LearnerInviteBatch(Settings, "คอร์ส", "a b/c?d", [Session()], isFirst: true);

        Assert.Equal("/learn/a%20b%2Fc%3Fd?tab=live", message.InApp.LinkPath);
        Assert.Contains($"{Base}/learn/a%20b%2Fc%3Fd?tab=live", Hrefs(message.BodyHtml));
    }

    // ---- Encoding / injection ------------------------------------------------------------------------

    [Fact]
    public void EveryUserValue_IsHtmlEncoded()
    {
        const string evilTitle = "<script>alert(1)</script>";
        const string evilCourse = "\"><img src=x onerror=alert(2)>";

        var messages = new[]
        {
            LiveEmailTemplates.LearnerInviteBatch(Settings, evilCourse, "c", [Session(evilTitle)], true),
            LiveEmailTemplates.LearnerSessionUpdated(Settings, evilCourse, "c", Session(evilTitle)),
            LiveEmailTemplates.LearnerSessionCancelled(Settings, evilCourse, "c", Session(evilTitle, cancelReason: evilTitle)),
            LiveEmailTemplates.LearnerLapsed(Settings, evilCourse, "c"),
            LiveEmailTemplates.LearnerReminder24h(Settings, evilCourse, "c", Session(evilTitle)),
            LiveEmailTemplates.LearnerReminder1h(Settings, evilCourse, "c", Session(evilTitle)),
            LiveEmailTemplates.InstructorBatch(Settings, evilCourse, [Session(evilTitle)], _ => false),
            LiveEmailTemplates.InstructorSessionUpdated(Settings, evilCourse, Session(evilTitle)),
            LiveEmailTemplates.InstructorSessionCancelled(Settings, evilCourse, Session(evilTitle, cancelReason: evilTitle)),
            LiveEmailTemplates.InstructorReminder(Settings, evilCourse, Session(evilTitle), roomReady: false, isOneHour: false),
            LiveEmailTemplates.InstructorReminder(Settings, evilCourse, Session(evilTitle), roomReady: true, isOneHour: true),
            LiveEmailTemplates.InstructorMeetingAlert(Settings, evilCourse, Session(evilTitle)),
        };

        foreach (var message in messages)
        {
            Assert.DoesNotContain("<script>", message.BodyHtml, StringComparison.Ordinal);
            Assert.DoesNotContain("<img", message.BodyHtml, StringComparison.Ordinal);
            Assert.Contains("&lt;", message.BodyHtml, StringComparison.Ordinal);
            AssertSafe(message);
        }
    }

    [Fact]
    public void CancelReason_IsEncoded_AndAppearsInTheBody()
    {
        var message = LiveEmailTemplates.LearnerSessionCancelled(Settings, "คอร์ส", "c", Session(cancelReason: "ผู้สอนป่วย & <b>ขออภัย</b>"));

        Assert.Contains("เหตุผล: ผู้สอนป่วย &amp; &lt;b&gt;ขออภัย&lt;/b&gt;", message.BodyHtml, StringComparison.Ordinal);
        Assert.Contains("ผู้สอนป่วย", message.InApp.Body, StringComparison.Ordinal);
    }

    [Fact]
    public void Subject_NeverCarriesLineBreaks_AndIsBounded()
    {
        var message = LiveEmailTemplates.LearnerSessionUpdated(Settings, "คอร์ส\r\nBcc: attacker@example.test", "c", Session("ชื่อ\r\nคาบ"));

        Assert.DoesNotContain('\r', message.Subject);
        Assert.DoesNotContain('\n', message.Subject);

        var longTitle = LiveEmailTemplates.LearnerSessionUpdated(Settings, new string('ก', 500), "c", Session(new string('ข', 500)));
        Assert.True(longTitle.Subject.Length < 250);
    }

    [Fact]
    public void ALayoutTitle_NeverContainsUserText()
    {
        var message = LiveEmailTemplates.LearnerInviteBatch(Settings, "{{Content}} <u>x</u>", "c", [Session("{{Title}}")], true);

        // The shared layout inserts its title unescaped and expands "{{...}}" tokens — so the <title> must be a fixed string.
        var title = Regex.Match(message.BodyHtml, "<title>(.*?)</title>", RegexOptions.Singleline).Groups[1].Value;
        Assert.Equal("ยืนยันตารางเรียนสด", title);
    }

    // ---- The room URL never appears -------------------------------------------------------------------

    [Fact]
    public void AMeetingLinkTypedIntoAnyFreeText_IsScrubbedFromEveryMessage()
    {
        const string room = "https://meet.google.com/abc-defg-hij";
        var session = Session($"คาบ {room}", description: $"เข้าที่ {room}", cancelReason: $"ลิงก์ {room} ใช้ไม่ได้");
        var course = $"คอร์ส zoom.us/j/12345 {room}";

        var messages = new[]
        {
            LiveEmailTemplates.LearnerInviteBatch(Settings, course, "c", [session], true),
            LiveEmailTemplates.LearnerSessionUpdated(Settings, course, "c", session),
            LiveEmailTemplates.LearnerSessionCancelled(Settings, course, "c", session),
            LiveEmailTemplates.LearnerLapsed(Settings, course, "c"),
            LiveEmailTemplates.LearnerReminder24h(Settings, course, "c", session),
            LiveEmailTemplates.LearnerReminder1h(Settings, course, "c", session),
            LiveEmailTemplates.InstructorBatch(Settings, course, [session], _ => true),
            LiveEmailTemplates.InstructorSessionUpdated(Settings, course, session),
            LiveEmailTemplates.InstructorSessionCancelled(Settings, course, session),
            LiveEmailTemplates.InstructorReminder(Settings, course, session, true, false),
            LiveEmailTemplates.InstructorMeetingAlert(Settings, course, session),
        };

        foreach (var message in messages)
        {
            var everything = message.Subject + message.BodyHtml + message.InApp.Title + message.InApp.Body + message.InApp.LinkPath;
            Assert.False(InviteHarness.ContainsMeetingHost(everything), $"{message.TemplateKey} leaked a meeting host");
            Assert.DoesNotContain("abc-defg-hij", everything, StringComparison.Ordinal);
        }
    }

    // ---- Other kinds ---------------------------------------------------------------------------------

    [Fact]
    public void LearnerSessionUpdated_ShowsTheNewTime_AndTheJoinLink()
    {
        var session = Session(id: Guid.NewGuid());

        var message = LiveEmailTemplates.LearnerSessionUpdated(Settings, "คอร์ส", "c", session);

        Assert.Equal("เปลี่ยนแปลงเวลาเรียนสด: คาบที่ 1 — คอร์ส", message.Subject);
        Assert.Equal("live-session-updated", message.TemplateKey);
        Assert.Equal("live.updated", message.InApp.Type);
        Assert.Contains(ThaiDateText.Format(session.StartsAtUtc, session.EndsAtUtc), message.BodyHtml, StringComparison.Ordinal);
        Assert.Contains($"{Base}/live/{session.SessionId:D}/join", Hrefs(message.BodyHtml));
    }

    [Fact]
    public void LearnerSessionCancelled_UsesTheContractSubject_AndHasNoJoinLink()
    {
        var message = LiveEmailTemplates.LearnerSessionCancelled(Settings, "คอร์ส", "c", Session());

        Assert.Equal("ยกเลิกคาบเรียนสด: คาบที่ 1 — คอร์ส", message.Subject);
        Assert.Equal("live-session-cancelled", message.TemplateKey);
        Assert.Equal("live.cancelled", message.InApp.Type);
        Assert.DoesNotContain("/join", message.BodyHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void LearnerLapsed_IsNeutral_AndDoesNotSayWhy()
    {
        var message = LiveEmailTemplates.LearnerLapsed(Settings, "คอร์ส", "c");

        Assert.Equal("คุณไม่ได้อยู่ในรายชื่อผู้เข้าร่วมคาบเรียนสด: คอร์ส", message.Subject);
        Assert.Equal("live-invite-lapsed", message.TemplateKey);
        Assert.DoesNotContain("คืนเงิน", message.BodyHtml, StringComparison.Ordinal);
        Assert.DoesNotContain("หมดอายุ", message.BodyHtml, StringComparison.Ordinal);
        Assert.Equal("/courses/c", message.InApp.LinkPath);
    }

    [Fact]
    public void LearnerReminder24h_TellsWhenTheRoomOpens()
    {
        // starts 10:00 Thai; the room opens 15 minutes earlier at 09:45
        var message = LiveEmailTemplates.LearnerReminder24h(Settings, "คอร์ส", "c", Session());

        Assert.Equal("พรุ่งนี้มีเรียนสด: คาบที่ 1 — คอร์ส", message.Subject);
        Assert.Equal("live-reminder-24h", message.TemplateKey);
        Assert.Equal("live.reminder_24h", message.InApp.Type);
        Assert.Contains("ตั้งแต่เวลา 09:45 น.", message.BodyHtml, StringComparison.Ordinal);
    }

    [Fact]
    public void LearnerReminder1h_UsesTheContractSubject()
    {
        var message = LiveEmailTemplates.LearnerReminder1h(Settings, "คอร์ส", "c", Session());

        Assert.Equal("อีก 1 ชั่วโมงเริ่มเรียนสด: คาบที่ 1", message.Subject);
        Assert.Equal("live-reminder-1h", message.TemplateKey);
        Assert.Equal("live.reminder_1h", message.InApp.Type);
    }

    [Fact]
    public void InstructorBatch_ListsTheSessions_WithRoomStatus_AndLinksToTheInstructorPage()
    {
        var ready = Session("พร้อม");
        var notReady = Session("ไม่พร้อม");

        var message = LiveEmailTemplates.InstructorBatch(Settings, "คอร์ส", [ready, notReady], s => s.SessionId == ready.SessionId);

        Assert.Equal("สร้างตารางสอนสดแล้ว: คอร์ส", message.Subject);
        Assert.Equal("live-instructor-batch", message.TemplateKey);
        Assert.Contains("ห้องพร้อมใช้งาน", message.BodyHtml, StringComparison.Ordinal);
        Assert.Contains("ยังไม่มีห้องประชุม", message.BodyHtml, StringComparison.Ordinal);
        Assert.Contains($"{Base}/instructor/sessions/{ready.SessionId:D}", Hrefs(message.BodyHtml));
        Assert.Equal($"/instructor/sessions/{ready.SessionId:D}", message.InApp.LinkPath);
        AssertSafe(message);
    }

    [Fact]
    public void InstructorReminders_LinkToTheInstructorPage_AndWarnWhenTheRoomIsNotReady()
    {
        var session = Session();

        var notReady = LiveEmailTemplates.InstructorReminder(Settings, "คอร์ส", session, roomReady: false, isOneHour: false);
        var ready = LiveEmailTemplates.InstructorReminder(Settings, "คอร์ส", session, roomReady: true, isOneHour: true);

        Assert.Equal("live-reminder-24h", notReady.TemplateKey);
        Assert.Contains("ห้องประชุมของคาบนี้ยังไม่พร้อม", notReady.BodyHtml, StringComparison.Ordinal);
        Assert.Equal("live-reminder-1h", ready.TemplateKey);
        Assert.Equal("อีก 1 ชั่วโมงเริ่มเรียนสด: คาบที่ 1", ready.Subject);
        Assert.Contains("ห้องประชุมพร้อมใช้งานแล้ว", ready.BodyHtml, StringComparison.Ordinal);
        Assert.Equal($"/instructor/sessions/{session.SessionId:D}", notReady.InApp.LinkPath);
    }

    [Fact]
    public void InstructorMeetingAlert_UsesTheContractSubject_AndExplainsTheFix()
    {
        var session = Session();

        var message = LiveEmailTemplates.InstructorMeetingAlert(Settings, "คอร์ส", session);

        Assert.Equal("ห้องประชุมของคาบ คาบที่ 1 ยังไม่พร้อม", message.Subject);
        Assert.Equal("live-meeting-alert", message.TemplateKey);
        Assert.Equal("live.meeting_alert", message.InApp.Type);
        Assert.Contains("วางลิงก์ห้องประชุม", message.BodyHtml, StringComparison.Ordinal);
        Assert.Contains("เชื่อม Google Calendar", message.BodyHtml, StringComparison.Ordinal);
        AssertSafe(message);
    }

    [Fact]
    public void InstructorUpdateAndCancel_UseTheInstructorTemplateKeys()
    {
        Assert.Equal("live-instructor-session-updated", LiveEmailTemplates.InstructorSessionUpdated(Settings, "c", Session()).TemplateKey);
        Assert.Equal("live-instructor-session-cancelled", LiveEmailTemplates.InstructorSessionCancelled(Settings, "c", Session()).TemplateKey);
    }
}
