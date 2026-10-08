using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Notification.Contracts;

namespace Siri.Modules.Live.Application;

/// <summary>
/// Default <see cref="IInstructorAlertSender"/>: stages an e-mail to the instructor through the Notification outbox
/// (<see cref="IEmailOutbox"/>) <b>and</b> an in-app notification (<see cref="IUserNotificationOutbox"/>, P11-04 §3.2/§7 WP-D) — both
/// commit with the caller's <c>SaveChanges</c>. The in-app notification is staged even when the instructor has no e-mail address.
/// Only content that is safe to send: every database-derived value is scrubbed of meeting links and HTML-encoded, links point at
/// the platform's own instructor pages, and no room URL is ever included.
/// </summary>
public sealed class InstructorAlertSender(
    IEmailOutbox emailOutbox,
    IUserNotificationOutbox notifications,
    IUserContactReader userContacts,
    IOptions<LiveOptions> options,
    ILogger<InstructorAlertSender> logger) : IInstructorAlertSender
{
    public const string ReconnectTemplateKey = "live-google-reconnect";
    public const string NeedsLinkTemplateKey = "live-meeting-needs-link";
    public const string FailedTemplateKey = "live-meeting-failed";

    /// <summary>In-app notification types (P11-04 contract §3.2).</summary>
    public const string ReconnectNotificationType = "live.google_reconnect";
    public const string NeedsLinkNotificationType = "live.meeting_needs_link";
    public const string FailedNotificationType = "live.meeting_failed";

    private const int SubjectTitleMaxLength = 80;

    public async Task GoogleReconnectNeededAsync(Guid instructorUserId, int affectedCount, CancellationToken cancellationToken)
    {
        var settingsLink = $"{options.Value.GetNormalizedPublicBaseUrl()}/instructor/live-settings";
        var affected = Math.Max(affectedCount, 0);

        var content = $"""
            <p>การเชื่อมต่อ Google Calendar ของคุณหมดอายุหรือถูกยกเลิก ระบบจึงสร้างหรืออัปเดตห้อง Google Meet ให้คาบสอนสดไม่ได้</p>
            <p>ขณะนี้มี <strong>{affected}</strong> คาบที่ยังต้องมีห้องประชุม กรุณาเชื่อมต่อ Google อีกครั้ง หรือวางลิงก์ห้องประชุมของคุณเองสำหรับแต่ละคาบ</p>
            {Button(settingsLink, "จัดการการเชื่อมต่อ Google")}
            """;

        await SendAsync(
            instructorUserId,
            "เชื่อม Google Calendar ใหม่เพื่อให้ห้อง Meet ทำงานต่อ",
            "เชื่อม Google Calendar ใหม่",
            content,
            ReconnectTemplateKey,
            new LiveInAppNotification(
                ReconnectNotificationType,
                "เชื่อม Google Calendar ใหม่",
                $"การเชื่อมต่อ Google Calendar ของคุณใช้ไม่ได้แล้ว มี {affected} คาบที่ยังต้องมีห้องประชุม",
                "/instructor/live-settings"),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task MeetingNeedsLinkAsync(
        Guid instructorUserId, Guid sessionId, string sessionTitle, string courseTitle, CancellationToken cancellationToken)
    {
        var sessionLink = SessionLink(sessionId);
        var title = Clean(sessionTitle);
        var course = Clean(courseTitle);

        var content = $"""
            <p>คาบสอนสด <strong>{Encode(title)}</strong> ของคอร์ส <strong>{Encode(course)}</strong> ยังไม่มีห้องประชุม</p>
            <p>คุณยังไม่ได้เชื่อมต่อ Google Calendar ระบบจึงสร้างห้องให้อัตโนมัติไม่ได้ กรุณาวางลิงก์ห้องประชุม (Google Meet, Zoom หรือ Microsoft Teams) สำหรับคาบนี้ เพื่อให้ผู้เรียนเข้าห้องได้</p>
            {Button(sessionLink, "วางลิงก์ห้องประชุม")}
            """;

        await SendAsync(
            instructorUserId,
            $"วางลิงก์ห้องประชุมสำหรับคาบ {SubjectTitle(title)}",
            "วางลิงก์ห้องประชุม",
            content,
            NeedsLinkTemplateKey,
            new LiveInAppNotification(
                NeedsLinkNotificationType,
                $"วางลิงก์ห้องประชุมสำหรับคาบ {SubjectTitle(title)}",
                $"{course} — คาบนี้ยังไม่มีห้องประชุม กรุณาวางลิงก์ห้องประชุม",
                LiveTemplateSettings.InstructorSessionPath(sessionId)),
            cancellationToken).ConfigureAwait(false);
    }

    public async Task MeetingFailedAsync(
        Guid instructorUserId, Guid sessionId, string sessionTitle, string courseTitle, CancellationToken cancellationToken)
    {
        var sessionLink = SessionLink(sessionId);
        var title = Clean(sessionTitle);
        var course = Clean(courseTitle);

        var content = $"""
            <p>ระบบพยายามสร้างห้อง Google Meet สำหรับคาบ <strong>{Encode(title)}</strong> ของคอร์ส <strong>{Encode(course)}</strong> หลายครั้งแล้วแต่ไม่สำเร็จ</p>
            <p>คุณสามารถลองสร้างห้องใหม่อีกครั้ง หรือวางลิงก์ห้องประชุมของคุณเองสำหรับคาบนี้ได้ที่หน้าจัดการคาบสอน</p>
            {Button(sessionLink, "จัดการคาบสอน")}
            """;

        await SendAsync(
            instructorUserId,
            $"สร้างห้อง Google Meet ไม่สำเร็จ — {SubjectTitle(title)}",
            "สร้างห้อง Google Meet ไม่สำเร็จ",
            content,
            FailedTemplateKey,
            new LiveInAppNotification(
                FailedNotificationType,
                $"สร้างห้อง Google Meet ไม่สำเร็จ — {SubjectTitle(title)}",
                $"{course} — ระบบสร้างห้องไม่สำเร็จ กรุณาลองใหม่หรือวางลิงก์ห้องประชุม",
                LiveTemplateSettings.InstructorSessionPath(sessionId)),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task SendAsync(
        Guid instructorUserId,
        string subject,
        string title,
        string contentHtml,
        string templateKey,
        LiveInAppNotification inApp,
        CancellationToken cancellationToken)
    {
        // In-app first: it needs no address, so an instructor without an e-mail still sees the alert in the bell.
        notifications.Stage(instructorUserId, inApp.Type, inApp.Title, inApp.Body, inApp.LinkPath);

        var email = await userContacts.GetEmailAsync(instructorUserId, cancellationToken).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(email))
        {
            // Never log the address itself — only that there was nobody to e-mail.
            logger.LogWarning("Live alert {TemplateKey} skipped: instructor {InstructorUserId} has no e-mail address.", templateKey, instructorUserId);
            return;
        }

        emailOutbox.Enqueue(email, subject, EmailTemplateRenderer.RenderLayout(title, contentHtml), templateKey);
    }

    private string SessionLink(Guid sessionId) => $"{options.Value.GetNormalizedPublicBaseUrl()}{LiveTemplateSettings.InstructorSessionPath(sessionId)}";

    /// <summary>Free text made safe for an outbound message: meeting links removed (the platform join URL is the only link a message may
    /// carry), control characters dropped.</summary>
    private string Clean(string? value) => LiveEmailTemplates.Clean(LiveTemplateSettings.From(options.Value), value);

    private static string Button(string href, string label) => $"""
        <p style="text-align:center; margin:32px 0;">
          <a href="{Encode(href)}"
             style="background-color:#111827; color:#ffffff; padding:12px 24px; border-radius:6px; text-decoration:none; display:inline-block;">
            {Encode(label)}
          </a>
        </p>
        """;

    private static string Encode(string? value) => WebUtility.HtmlEncode(value ?? string.Empty);

    /// <summary>Single-line, bounded text for an e-mail subject (a title containing CR/LF must not be able to inject headers).</summary>
    internal static string SubjectTitle(string? title)
    {
        var builder = new StringBuilder(Math.Min(title?.Length ?? 0, SubjectTitleMaxLength));
        foreach (var c in title ?? string.Empty)
        {
            builder.Append(char.IsControl(c) ? ' ' : c);
            if (builder.Length >= SubjectTitleMaxLength)
            {
                break;
            }
        }

        return builder.ToString().Trim();
    }
}
