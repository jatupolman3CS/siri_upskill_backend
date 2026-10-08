using System.Globalization;
using System.Net;
using System.Text;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Notification.Contracts;

namespace Siri.Modules.Live.Application;

/// <summary>The in-app (bell) counterpart of an e-mail — plain text, rendered by the frontend as text, never as HTML.
/// <paramref name="LinkPath"/> is an app-relative path (<c>/learn/{slug}?tab=live</c>, <c>/instructor/sessions/{id}</c>).</summary>
public sealed record LiveInAppNotification(string Type, string Title, string Body, string LinkPath);

/// <summary>One outbound message: the e-mail (Thai) plus its in-app twin. <c>TemplateKey</c> identifies the template in the
/// outbox for diagnostics.</summary>
public sealed record LiveMessage(string Subject, string BodyHtml, string TemplateKey, LiveInAppNotification InApp);

/// <summary>Where the platform's links point and how early the room opens — the only things the templates need from
/// <see cref="LiveOptions"/>.</summary>
public sealed record LiveTemplateSettings(string PublicBaseUrl, int JoinWindowBeforeMinutes, IReadOnlyList<string> MeetingHosts)
{
    /// <summary>Builds the settings from the bound options.</summary>
    public static LiveTemplateSettings From(LiveOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);
        return new LiveTemplateSettings(options.GetNormalizedPublicBaseUrl(), options.JoinWindowBeforeMinutes, options.GetEffectiveAllowedMeetingHosts());
    }

    /// <summary>The platform join link — the <b>only</b> room link that ever appears in an e-mail or calendar file.</summary>
    public string JoinUrl(Guid sessionId) => $"{PublicBaseUrl}/live/{sessionId:D}/join";

    /// <summary>The instructor's page for one session.</summary>
    public string InstructorSessionUrl(Guid sessionId) => $"{PublicBaseUrl}{InstructorSessionPath(sessionId)}";

    public static string InstructorSessionPath(Guid sessionId) => $"/instructor/sessions/{sessionId:D}";

    public static string LearnerLiveTabPath(string courseSlug) => $"/learn/{Uri.EscapeDataString(courseSlug)}?tab=live";

    public static string CoursePath(string courseSlug) => $"/courses/{Uri.EscapeDataString(courseSlug)}";
}

/// <summary>
/// Thai e-mail and in-app texts for the live-session invite/reminder flow (docs/contracts/
/// P11-04-live-invites-ics-reminders.md §4.3). Pure and static — no I/O — so every text is unit-testable.
/// <para>
/// <b>Safety rules enforced here:</b> every value that came from the database or a user (course title, session title,
/// description, cancel reason, a recipient's name) is passed through <see cref="MeetingUrlScrubber"/> and
/// <see cref="WebUtility.HtmlEncode(string)"/> before it reaches HTML; subjects are single-line and bounded; the layout
/// title is a fixed string (the shared layout inserts it unescaped); and the only links are the platform's own
/// (<see cref="LiveTemplateSettings.JoinUrl"/>, the instructor page, the course/learn pages) — no template takes a room URL
/// as input at all.
/// </para>
/// </summary>
public static class LiveEmailTemplates
{
    public const string InviteBatchKey = "live-invite-batch";
    public const string SessionUpdatedKey = "live-session-updated";
    public const string SessionCancelledKey = "live-session-cancelled";
    public const string InviteLapsedKey = "live-invite-lapsed";
    public const string Reminder24hKey = "live-reminder-24h";
    public const string Reminder1hKey = "live-reminder-1h";
    public const string InstructorBatchKey = "live-instructor-batch";
    public const string InstructorUpdatedKey = "live-instructor-session-updated";
    public const string InstructorCancelledKey = "live-instructor-session-cancelled";
    public const string MeetingAlertKey = "live-meeting-alert";

    public const string InAppInvite = "live.invite";
    public const string InAppUpdated = "live.updated";
    public const string InAppCancelled = "live.cancelled";
    public const string InAppReminder24h = "live.reminder_24h";
    public const string InAppReminder1h = "live.reminder_1h";
    public const string InAppMeetingAlert = "live.meeting_alert";

    /// <summary>Most rows shown in a batch table (the rest is summarised as "and n more").</summary>
    public const int MaxTableRows = 30;

    /// <summary>Most sessions one batch e-mail / calendar file carries (a longer list is split into several e-mails).</summary>
    public const int MaxSessionsPerEmail = 50;

    private const string LearnerWarning = "ลิงก์เข้าห้องเรียนใช้ได้เฉพาะบัญชีของคุณ — โปรดอย่าส่งต่อ";

    // ---- Learner ------------------------------------------------------------------------------

    /// <summary>The purchase-day (or "sessions added later") e-mail: every upcoming session of the course in one table.
    /// <paramref name="isFirst"/> = the learner had no earlier invite in this course.</summary>
    public static LiveMessage LearnerInviteBatch(
        LiveTemplateSettings settings, string courseTitle, string courseSlug, IReadOnlyList<LiveSessionContext> sessions, bool isFirst)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sessions);

        var course = Clean(settings, courseTitle);
        var heading = isFirst ? "ยืนยันตารางเรียนสด" : "เพิ่มคาบเรียนสดใหม่";
        var subject = $"{heading}: {SubjectText(course)}";

        var intro = isFirst
            ? $"คุณลงทะเบียนคอร์ส <strong>{Encode(course)}</strong> เรียบร้อยแล้ว คอร์สนี้มีคาบเรียนสด {sessions.Count} คาบตามตารางด้านล่าง ไฟล์ปฏิทินแนบมากับอีเมลนี้ เพิ่มลงปฏิทินของคุณได้ทันที"
            : $"คอร์ส <strong>{Encode(course)}</strong> มีคาบเรียนสดเพิ่มเติม {sessions.Count} คาบตามตารางด้านล่าง ไฟล์ปฏิทินแนบมากับอีเมลนี้";

        var content = new StringBuilder()
            .Append("<p>").Append(intro).Append("</p>")
            .Append(SessionTable(settings, sessions, "เข้าห้องเรียน", s => settings.JoinUrl(s.SessionId)))
            .Append("<p>เปิดลิงก์เข้าห้องเรียนได้ก่อนเวลาเริ่ม ").Append(settings.JoinWindowBeforeMinutes.ToString(CultureInfo.InvariantCulture)).Append(" นาที</p>")
            .Append(Button($"{settings.PublicBaseUrl}{LiveTemplateSettings.LearnerLiveTabPath(courseSlug)}", "ดูตารางเรียนสดของฉัน"))
            .Append(Notice(LearnerWarning))
            .ToString();

        var first = sessions.Count > 0 ? ThaiDateText.FormatStart(sessions[0].StartsAtUtc) : string.Empty;
        return new LiveMessage(
            subject,
            Page(heading, content),
            InviteBatchKey,
            new LiveInAppNotification(
                InAppInvite,
                $"{heading}: {SubjectText(course)}",
                $"{course} มีคาบเรียนสด {sessions.Count} คาบ คาบแรก {first}",
                LiveTemplateSettings.LearnerLiveTabPath(courseSlug)));
    }

    /// <summary>A session's time or title changed (REQUEST with a higher SEQUENCE).</summary>
    public static LiveMessage LearnerSessionUpdated(
        LiveTemplateSettings settings, string courseTitle, string courseSlug, LiveSessionContext session)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(session);

        var course = Clean(settings, courseTitle);
        var title = Clean(settings, session.Title);
        var when = ThaiDateText.Format(session.StartsAtUtc, session.EndsAtUtc);

        var content = new StringBuilder()
            .Append("<p>คาบเรียนสด <strong>").Append(Encode(title)).Append("</strong> ของคอร์ส <strong>").Append(Encode(course)).Append("</strong> มีการเปลี่ยนแปลง</p>")
            .Append("<p>เวลาใหม่: <strong>").Append(Encode(when)).Append("</strong></p>")
            .Append(Button(settings.JoinUrl(session.SessionId), "เข้าห้องเรียน"))
            .Append("<p>ไฟล์ปฏิทินที่แนบมาจะอัปเดตนัดหมายเดิมในปฏิทินของคุณ</p>")
            .Append(Notice(LearnerWarning))
            .ToString();

        return new LiveMessage(
            $"เปลี่ยนแปลงเวลาเรียนสด: {SubjectText(title)} — {SubjectText(course)}",
            Page("เปลี่ยนแปลงเวลาเรียนสด", content),
            SessionUpdatedKey,
            new LiveInAppNotification(
                InAppUpdated,
                $"เปลี่ยนแปลงเวลาเรียนสด: {SubjectText(title)}",
                $"{course} — เวลาใหม่ {when}",
                LiveTemplateSettings.LearnerLiveTabPath(courseSlug)));
    }

    /// <summary>A session was cancelled (CANCEL).</summary>
    public static LiveMessage LearnerSessionCancelled(
        LiveTemplateSettings settings, string courseTitle, string courseSlug, LiveSessionContext session)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(session);

        var course = Clean(settings, courseTitle);
        var title = Clean(settings, session.Title);
        var reason = CleanOptional(settings, session.CancelReason);
        var when = ThaiDateText.Format(session.StartsAtUtc, session.EndsAtUtc);

        var content = new StringBuilder()
            .Append("<p>คาบเรียนสด <strong>").Append(Encode(title)).Append("</strong> ของคอร์ส <strong>").Append(Encode(course)).Append("</strong> ถูกยกเลิก</p>")
            .Append("<p>เวลาเดิม: ").Append(Encode(when)).Append("</p>");
        if (!string.IsNullOrWhiteSpace(reason))
        {
            content.Append("<p>เหตุผล: ").Append(Encode(reason)).Append("</p>");
        }

        content.Append("<p>ไฟล์ปฏิทินที่แนบมาจะนำนัดหมายนี้ออกจากปฏิทินของคุณ</p>");

        return new LiveMessage(
            $"ยกเลิกคาบเรียนสด: {SubjectText(title)} — {SubjectText(course)}",
            Page("ยกเลิกคาบเรียนสด", content.ToString()),
            SessionCancelledKey,
            new LiveInAppNotification(
                InAppCancelled,
                $"ยกเลิกคาบเรียนสด: {SubjectText(title)}",
                string.IsNullOrWhiteSpace(reason) ? $"{course} — คาบนี้ถูกยกเลิก" : $"{course} — คาบนี้ถูกยกเลิก เหตุผล: {reason}",
                LiveTemplateSettings.LearnerLiveTabPath(courseSlug)));
    }

    /// <summary>The learner is no longer on the participant list (refund/expiry/revoke — deliberately not said which).</summary>
    public static LiveMessage LearnerLapsed(LiveTemplateSettings settings, string courseTitle, string courseSlug)
    {
        ArgumentNullException.ThrowIfNull(settings);

        var course = Clean(settings, courseTitle);
        var content = new StringBuilder()
            .Append("<p>คุณไม่ได้อยู่ในรายชื่อผู้เข้าร่วมคาบเรียนสดของคอร์ส <strong>").Append(Encode(course)).Append("</strong> แล้ว</p>")
            .Append("<p>ไฟล์ปฏิทินที่แนบมาจะนำนัดหมายเรียนสดของคอร์สนี้ออกจากปฏิทินของคุณ หากคุณคิดว่าเกิดข้อผิดพลาด กรุณาติดต่อฝ่ายสนับสนุน</p>")
            .ToString();

        return new LiveMessage(
            $"คุณไม่ได้อยู่ในรายชื่อผู้เข้าร่วมคาบเรียนสด: {SubjectText(course)}",
            Page("ไม่อยู่ในรายชื่อผู้เข้าร่วมคาบเรียนสด", content),
            InviteLapsedKey,
            new LiveInAppNotification(
                InAppCancelled,
                $"ไม่อยู่ในรายชื่อผู้เข้าร่วมเรียนสด: {SubjectText(course)}",
                $"คุณไม่ได้อยู่ในรายชื่อผู้เข้าร่วมคาบเรียนสดของคอร์ส {course} แล้ว",
                LiveTemplateSettings.CoursePath(courseSlug)));
    }

    /// <summary>The "tomorrow" reminder (24 hours ahead).</summary>
    public static LiveMessage LearnerReminder24h(
        LiveTemplateSettings settings, string courseTitle, string courseSlug, LiveSessionContext session)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(session);

        var course = Clean(settings, courseTitle);
        var title = Clean(settings, session.Title);
        var when = ThaiDateText.Format(session.StartsAtUtc, session.EndsAtUtc);
        var opens = ThaiDateText.FormatTimeOfDay(session.StartsAtUtc.AddMinutes(-settings.JoinWindowBeforeMinutes));

        var content = new StringBuilder()
            .Append("<p>พรุ่งนี้คุณมีคาบเรียนสด <strong>").Append(Encode(title)).Append("</strong> ของคอร์ส <strong>").Append(Encode(course)).Append("</strong></p>")
            .Append("<p>เวลา: <strong>").Append(Encode(when)).Append("</strong></p>")
            .Append("<p>ห้องเรียนเปิดให้เข้าได้ตั้งแต่เวลา ").Append(Encode(opens)).Append("</p>")
            .Append(Button(settings.JoinUrl(session.SessionId), "เข้าห้องเรียน"))
            .Append(Notice(LearnerWarning))
            .ToString();

        return new LiveMessage(
            $"พรุ่งนี้มีเรียนสด: {SubjectText(title)} — {SubjectText(course)}",
            Page("พรุ่งนี้มีเรียนสด", content),
            Reminder24hKey,
            new LiveInAppNotification(
                InAppReminder24h,
                $"พรุ่งนี้มีเรียนสด: {SubjectText(title)}",
                $"{course} — {when}",
                LiveTemplateSettings.LearnerLiveTabPath(courseSlug)));
    }

    /// <summary>The "one hour to go" reminder.</summary>
    public static LiveMessage LearnerReminder1h(
        LiveTemplateSettings settings, string courseTitle, string courseSlug, LiveSessionContext session)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(session);

        var course = Clean(settings, courseTitle);
        var title = Clean(settings, session.Title);
        var when = ThaiDateText.Format(session.StartsAtUtc, session.EndsAtUtc);

        var content = new StringBuilder()
            .Append("<p>อีก 1 ชั่วโมงจะเริ่มคาบเรียนสด <strong>").Append(Encode(title)).Append("</strong> ของคอร์ส <strong>").Append(Encode(course)).Append("</strong></p>")
            .Append("<p>เวลา: <strong>").Append(Encode(when)).Append("</strong></p>")
            .Append(Button(settings.JoinUrl(session.SessionId), "เข้าห้องเรียน"))
            .Append(Notice(LearnerWarning))
            .ToString();

        return new LiveMessage(
            $"อีก 1 ชั่วโมงเริ่มเรียนสด: {SubjectText(title)}",
            Page("อีก 1 ชั่วโมงเริ่มเรียนสด", content),
            Reminder1hKey,
            new LiveInAppNotification(
                InAppReminder1h,
                $"อีก 1 ชั่วโมงเริ่มเรียนสด: {SubjectText(title)}",
                $"{course} — {when}",
                LiveTemplateSettings.LearnerLiveTabPath(courseSlug)));
    }

    // ---- Instructor ---------------------------------------------------------------------------

    /// <summary>The instructor's batch: the sessions that were scheduled, each with its room status. <paramref name="roomReady"/>
    /// says whether a usable room exists for a session.</summary>
    public static LiveMessage InstructorBatch(
        LiveTemplateSettings settings,
        string courseTitle,
        IReadOnlyList<LiveSessionContext> sessions,
        Func<LiveSessionContext, bool> roomReady)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(sessions);
        ArgumentNullException.ThrowIfNull(roomReady);

        var course = Clean(settings, courseTitle);
        var notReady = sessions.Count(s => !roomReady(s));

        var content = new StringBuilder()
            .Append("<p>ระบบบันทึกตารางสอนสดของคอร์ส <strong>").Append(Encode(course)).Append("</strong> จำนวน ").Append(sessions.Count.ToString(CultureInfo.InvariantCulture)).Append(" คาบแล้ว</p>")
            .Append(SessionTable(
                settings,
                sessions,
                "จัดการคาบ",
                s => settings.InstructorSessionUrl(s.SessionId),
                s => roomReady(s) ? "ห้องพร้อมใช้งาน" : "ยังไม่มีห้องประชุม"));

        if (notReady > 0)
        {
            content.Append("<p>มี <strong>").Append(notReady.ToString(CultureInfo.InvariantCulture))
                .Append("</strong> คาบที่ยังไม่มีห้องประชุม กรุณาเชื่อม Google Calendar หรือวางลิงก์ห้องประชุม (Google Meet, Zoom หรือ Microsoft Teams) ที่หน้าจัดการคาบ ก่อนวันสอน</p>");
        }

        content.Append("<p>ผู้เรียนเข้าห้องผ่านลิงก์ของแพลตฟอร์มเท่านั้น ลิงก์ห้องประชุมจริงจะไม่ถูกส่งทางอีเมลหรือไฟล์ปฏิทิน</p>");

        var firstSession = sessions.Count > 0 ? sessions[0] : null;
        return new LiveMessage(
            $"สร้างตารางสอนสดแล้ว: {SubjectText(course)}",
            Page("สร้างตารางสอนสดแล้ว", content.ToString()),
            InstructorBatchKey,
            new LiveInAppNotification(
                InAppInvite,
                $"สร้างตารางสอนสดแล้ว: {SubjectText(course)}",
                notReady > 0
                    ? $"{course} — {sessions.Count} คาบ (ยังไม่มีห้องประชุม {notReady} คาบ)"
                    : $"{course} — {sessions.Count} คาบ",
                firstSession is null ? "/instructor/sessions" : LiveTemplateSettings.InstructorSessionPath(firstSession.SessionId)));
    }

    /// <summary>Instructor: a session's time/title changed (only sent when the room is not a Google event on their own calendar).</summary>
    public static LiveMessage InstructorSessionUpdated(LiveTemplateSettings settings, string courseTitle, LiveSessionContext session)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(session);

        var course = Clean(settings, courseTitle);
        var title = Clean(settings, session.Title);
        var when = ThaiDateText.Format(session.StartsAtUtc, session.EndsAtUtc);

        var content = new StringBuilder()
            .Append("<p>คาบสอนสด <strong>").Append(Encode(title)).Append("</strong> ของคอร์ส <strong>").Append(Encode(course)).Append("</strong> ถูกเปลี่ยนแปลง</p>")
            .Append("<p>เวลาใหม่: <strong>").Append(Encode(when)).Append("</strong></p>")
            .Append(Button(settings.InstructorSessionUrl(session.SessionId), "จัดการคาบ"))
            .ToString();

        return new LiveMessage(
            $"เปลี่ยนแปลงเวลาเรียนสด: {SubjectText(title)} — {SubjectText(course)}",
            Page("เปลี่ยนแปลงเวลาเรียนสด", content),
            InstructorUpdatedKey,
            new LiveInAppNotification(
                InAppUpdated,
                $"เปลี่ยนแปลงเวลาเรียนสด: {SubjectText(title)}",
                $"{course} — เวลาใหม่ {when}",
                LiveTemplateSettings.InstructorSessionPath(session.SessionId)));
    }

    /// <summary>Instructor: a session was cancelled (only sent when the room is not a Google event on their own calendar).</summary>
    public static LiveMessage InstructorSessionCancelled(LiveTemplateSettings settings, string courseTitle, LiveSessionContext session)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(session);

        var course = Clean(settings, courseTitle);
        var title = Clean(settings, session.Title);
        var reason = CleanOptional(settings, session.CancelReason);

        var content = new StringBuilder()
            .Append("<p>คาบสอนสด <strong>").Append(Encode(title)).Append("</strong> ของคอร์ส <strong>").Append(Encode(course)).Append("</strong> ถูกยกเลิก</p>");
        if (!string.IsNullOrWhiteSpace(reason))
        {
            content.Append("<p>เหตุผล: ").Append(Encode(reason)).Append("</p>");
        }

        content.Append("<p>ไฟล์ปฏิทินที่แนบมาจะนำนัดหมายนี้ออกจากปฏิทินของคุณ</p>");

        return new LiveMessage(
            $"ยกเลิกคาบเรียนสด: {SubjectText(title)} — {SubjectText(course)}",
            Page("ยกเลิกคาบเรียนสด", content.ToString()),
            InstructorCancelledKey,
            new LiveInAppNotification(
                InAppCancelled,
                $"ยกเลิกคาบเรียนสด: {SubjectText(title)}",
                $"{course} — คาบนี้ถูกยกเลิก",
                LiveTemplateSettings.InstructorSessionPath(session.SessionId)));
    }

    /// <summary>Instructor reminder (24 hours or 1 hour ahead). <paramref name="roomReady"/> adds a warning when the room is missing.</summary>
    public static LiveMessage InstructorReminder(
        LiveTemplateSettings settings, string courseTitle, LiveSessionContext session, bool roomReady, bool isOneHour)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(session);

        var course = Clean(settings, courseTitle);
        var title = Clean(settings, session.Title);
        var when = ThaiDateText.Format(session.StartsAtUtc, session.EndsAtUtc);

        var lead = isOneHour
            ? "อีก 1 ชั่วโมงคุณมีคาบสอนสด"
            : "พรุ่งนี้คุณมีคาบสอนสด";

        var content = new StringBuilder()
            .Append("<p>").Append(lead).Append(" <strong>").Append(Encode(title)).Append("</strong> ของคอร์ส <strong>").Append(Encode(course)).Append("</strong></p>")
            .Append("<p>เวลา: <strong>").Append(Encode(when)).Append("</strong></p>")
            .Append(roomReady
                ? "<p>ห้องประชุมพร้อมใช้งานแล้ว</p>"
                : "<p><strong>ห้องประชุมของคาบนี้ยังไม่พร้อม</strong> กรุณาตรวจสอบที่หน้าจัดการคาบ</p>")
            .Append(Button(settings.InstructorSessionUrl(session.SessionId), "จัดการคาบ"))
            .ToString();

        return new LiveMessage(
            isOneHour
                ? $"อีก 1 ชั่วโมงเริ่มเรียนสด: {SubjectText(title)}"
                : $"พรุ่งนี้มีเรียนสด: {SubjectText(title)} — {SubjectText(course)}",
            Page(isOneHour ? "อีก 1 ชั่วโมงเริ่มเรียนสด" : "พรุ่งนี้มีเรียนสด", content),
            isOneHour ? Reminder1hKey : Reminder24hKey,
            new LiveInAppNotification(
                isOneHour ? InAppReminder1h : InAppReminder24h,
                isOneHour ? $"อีก 1 ชั่วโมงเริ่มสอนสด: {SubjectText(title)}" : $"พรุ่งนี้มีสอนสด: {SubjectText(title)}",
                $"{course} — {when}",
                LiveTemplateSettings.InstructorSessionPath(session.SessionId)));
    }

    /// <summary>Instructor: the room for a session starting within a day is still not ready.</summary>
    public static LiveMessage InstructorMeetingAlert(LiveTemplateSettings settings, string courseTitle, LiveSessionContext session)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(session);

        var course = Clean(settings, courseTitle);
        var title = Clean(settings, session.Title);
        var when = ThaiDateText.Format(session.StartsAtUtc, session.EndsAtUtc);

        var content = new StringBuilder()
            .Append("<p>คาบสอนสด <strong>").Append(Encode(title)).Append("</strong> ของคอร์ส <strong>").Append(Encode(course)).Append("</strong> จะเริ่มภายใน 24 ชั่วโมง แต่ยังไม่มีห้องประชุมที่ใช้งานได้</p>")
            .Append("<p>เวลา: <strong>").Append(Encode(when)).Append("</strong></p>")
            .Append("<p>ผู้เรียนจะเข้าห้องไม่ได้จนกว่าห้องจะพร้อม วิธีแก้: เชื่อม Google Calendar ใหม่เพื่อให้ระบบสร้างห้อง Google Meet ให้อัตโนมัติ หรือวางลิงก์ห้องประชุม (Google Meet, Zoom หรือ Microsoft Teams) สำหรับคาบนี้ที่หน้าจัดการคาบ</p>")
            .Append(Button(settings.InstructorSessionUrl(session.SessionId), "จัดการคาบ"))
            .ToString();

        return new LiveMessage(
            $"ห้องประชุมของคาบ {SubjectText(title)} ยังไม่พร้อม",
            Page("ห้องประชุมยังไม่พร้อม", content),
            MeetingAlertKey,
            new LiveInAppNotification(
                InAppMeetingAlert,
                $"ห้องประชุมของคาบ {SubjectText(title)} ยังไม่พร้อม",
                $"{course} — เริ่ม {when} กรุณาเชื่อม Google หรือวางลิงก์ห้องประชุม",
                LiveTemplateSettings.InstructorSessionPath(session.SessionId)));
    }

    // ---- Helpers ------------------------------------------------------------------------------

    /// <summary>Free text made safe for an outbound message: meeting links removed, control characters dropped.</summary>
    internal static string Clean(LiveTemplateSettings settings, string? value) =>
        StripControl(MeetingUrlScrubber.ScrubRequired(value ?? string.Empty, settings.MeetingHosts));

    internal static string? CleanOptional(LiveTemplateSettings settings, string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : Clean(settings, value);

    /// <summary>Single-line, bounded text for an e-mail subject or an in-app title (CR/LF in a title can never inject headers).</summary>
    internal static string SubjectText(string? value) => InstructorAlertSender.SubjectTitle(value);

    private static string StripControl(string value)
    {
        if (!value.Any(char.IsControl))
        {
            return value;
        }

        var builder = new StringBuilder(value.Length);
        foreach (var c in value)
        {
            builder.Append(char.IsControl(c) ? ' ' : c);
        }

        return builder.ToString();
    }

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    /// <summary>The shared layout inserts its title unescaped, so only fixed strings are ever passed (callers pass literals).</summary>
    private static string Page(string title, string contentHtml) => EmailTemplateRenderer.RenderLayout(title, contentHtml);

    private static string Notice(string text) =>
        $"<p style=\"color:#6b7280; font-size:13px;\">{Encode(text)}</p>";

    private static string Button(string href, string label) => $"""
        <p style="text-align:center; margin:28px 0;">
          <a href="{Encode(href)}"
             style="background-color:#111827; color:#ffffff; padding:12px 24px; border-radius:6px; text-decoration:none; display:inline-block;">
            {Encode(label)}
          </a>
        </p>
        """;

    private static string SessionTable(
        LiveTemplateSettings settings,
        IReadOnlyList<LiveSessionContext> sessions,
        string linkLabel,
        Func<LiveSessionContext, string> linkFor,
        Func<LiveSessionContext, string>? statusFor = null)
    {
        var builder = new StringBuilder();
        builder.Append("<table role=\"presentation\" width=\"100%\" cellpadding=\"6\" cellspacing=\"0\" style=\"border-collapse:collapse; margin:16px 0; font-size:14px;\">");

        foreach (var session in sessions.Take(MaxTableRows))
        {
            builder.Append("<tr style=\"border-bottom:1px solid #e5e7eb;\"><td valign=\"top\">")
                .Append("<strong>").Append(Encode(Clean(settings, session.Title))).Append("</strong><br />")
                .Append(Encode(ThaiDateText.Format(session.StartsAtUtc, session.EndsAtUtc)));

            if (statusFor is not null)
            {
                builder.Append("<br /><span style=\"color:#6b7280;\">").Append(Encode(statusFor(session))).Append("</span>");
            }

            builder.Append("</td><td valign=\"top\" align=\"right\"><a href=\"").Append(Encode(linkFor(session))).Append("\">").Append(Encode(linkLabel)).Append("</a></td></tr>");
        }

        builder.Append("</table>");

        if (sessions.Count > MaxTableRows)
        {
            builder.Append("<p>และอีก ").Append((sessions.Count - MaxTableRows).ToString(CultureInfo.InvariantCulture)).Append(" คาบ ดูตารางทั้งหมดได้ในหน้าตารางเรียนสด</p>");
        }

        return builder.ToString();
    }
}
