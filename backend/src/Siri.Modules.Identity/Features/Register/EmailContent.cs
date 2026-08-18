using System.Net;
using Siri.Modules.Notification.Contracts;

namespace Siri.Modules.Identity.Features.Register;

/// <summary>
/// Confirmation-email copy for a freshly registered account. Builds on top of the Notification
/// module's shared HTML shell (<see cref="EmailTemplateRenderer"/>) instead of hand-rolling a second
/// layout — same shape as <c>GenericNotificationEmailTemplate</c> in that module, just reached
/// through the Contracts/ surface since this type lives in a different module.
/// </summary>
internal static class RegisterEmailContent
{
    /// <summary>Matches <c>EmailOutboxMessage.TemplateKey</c> for messages built from this template.</summary>
    public const string TemplateKey = "identity-email-confirmation";

    public const string Subject = "ยืนยันอีเมลของคุณ - SIRI UpSkill";

    /// <summary>
    /// <paramref name="displayName"/> is plain text and gets HTML-encoded (untrusted — a user's own
    /// display name). <paramref name="confirmationLink"/> is also encoded even though it is
    /// server-built from a hex token and a configured base URL — defense in depth, not because
    /// either input is expected to contain markup.
    /// </summary>
    public static string Render(string displayName, string confirmationLink)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(confirmationLink);

        var encodedName = WebUtility.HtmlEncode(displayName);
        var encodedLink = WebUtility.HtmlEncode(confirmationLink);

        var content = $"""
            <p>สวัสดีคุณ {encodedName},</p>
            <p>ขอบคุณที่สมัครสมาชิก SIRI UpSkill กรุณากดปุ่มด้านล่างเพื่อยืนยันอีเมลของคุณ ลิงก์นี้จะหมดอายุภายใน 24 ชั่วโมง</p>
            <p style="text-align:center; margin:32px 0;">
              <a href="{encodedLink}"
                 style="background-color:#111827; color:#ffffff; padding:12px 24px; border-radius:6px; text-decoration:none; display:inline-block;">
                ยืนยันอีเมล
              </a>
            </p>
            <p>หากปุ่มด้านบนใช้งานไม่ได้ ให้คัดลอกลิงก์นี้ไปวางในเบราว์เซอร์ของคุณ:<br />
              <span style="word-break:break-all;">{encodedLink}</span>
            </p>
            <p>หากคุณไม่ได้เป็นผู้สมัครสมาชิกนี้ กรุณาเพิกเฉยต่ออีเมลฉบับนี้ — จะไม่มีการสร้างบัญชีใด ๆ หากไม่มีการกดยืนยัน</p>
            """;

        return EmailTemplateRenderer.RenderLayout("ยืนยันอีเมลของคุณ", content);
    }
}
