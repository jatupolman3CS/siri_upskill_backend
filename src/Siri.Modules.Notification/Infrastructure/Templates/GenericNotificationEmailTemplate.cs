using System.Net;

namespace Siri.Modules.Notification.Infrastructure.Templates;

/// <summary>
/// One concrete template built on top of <see cref="EmailLayout"/>, proving the layout mechanism
/// works end to end. Deliberately generic (a simple "here's a notification" message) — the real
/// content templates (email confirmation, password reset, ...) are separate, later tasks (P0-15,
/// P0-21) that will follow this same "render on top of <see cref="EmailLayout"/>" shape.
/// </summary>
public static class GenericNotificationEmailTemplate
{
    /// <summary>Matches <see cref="Domain.EMAIL_OUTBOX_MESSAGE.TemplateKey"/> for messages built from
    /// this template.</summary>
    public const string Key = "generic-notification";

    /// <summary>
    /// <paramref name="recipientDisplayName"/> is plain text and gets HTML-encoded here (untrusted —
    /// it is a user's own display name). <paramref name="messageBodyHtml"/> is inserted as-is: it is
    /// the caller-composed message body and is expected to already be safe HTML, same rule as
    /// <see cref="EmailLayout.Render"/>.
    /// </summary>
    public static string Render(string recipientDisplayName, string messageBodyHtml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipientDisplayName);
        ArgumentNullException.ThrowIfNull(messageBodyHtml);

        var content = $"""
            <p>สวัสดีคุณ {WebUtility.HtmlEncode(recipientDisplayName)},</p>
            <p>{messageBodyHtml}</p>
            <p>ขอบคุณที่ใช้งาน SIRI UpSkill</p>
            """;

        return EmailLayout.Render("แจ้งเตือนจาก SIRI UpSkill", content);
    }
}
