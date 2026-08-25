using System.Net;
using Siri.Modules.Notification.Contracts;

namespace Siri.Modules.Identity.Features.ForgotPassword;

/// <summary>
/// Password-reset-link email copy. Builds on top of the Notification module's shared HTML shell
/// (<see cref="EmailTemplateRenderer"/>) instead of hand-rolling a second layout — same shape as
/// <c>Register/EmailContent.cs</c>'s <c>RegisterEmailContent</c>.
/// </summary>
internal static class ForgotPasswordEmailContent
{
    /// <summary>Matches <c>EmailOutboxMessage.TemplateKey</c> for messages built from this template.</summary>
    public const string TemplateKey = "identity-password-reset";

    public const string Subject = "ตั้งรหัสผ่านใหม่สำหรับบัญชีของคุณ - SIRI UpSkill";

    /// <summary>
    /// <paramref name="displayName"/> is plain text and gets HTML-encoded (untrusted — a user's own
    /// display name). <paramref name="resetLink"/> is also encoded even though it is server-built from
    /// a hex token and a configured base URL — same defense-in-depth reasoning
    /// <c>RegisterEmailContent.Render</c>'s doc comment gives for its confirmation link.
    /// </summary>
    public static string Render(string displayName, string resetLink)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(resetLink);

        var encodedName = WebUtility.HtmlEncode(displayName);
        var encodedLink = WebUtility.HtmlEncode(resetLink);

        var content = $"""
            <p>สวัสดีคุณ {encodedName},</p>
            <p>เราได้รับคำขอตั้งรหัสผ่านใหม่สำหรับบัญชีนี้ กรุณากดปุ่มด้านล่างเพื่อตั้งรหัสผ่านใหม่ ลิงก์นี้จะหมดอายุภายใน 1 ชั่วโมง</p>
            <p style="text-align:center; margin:32px 0;">
              <a href="{encodedLink}"
                 style="background-color:#111827; color:#ffffff; padding:12px 24px; border-radius:6px; text-decoration:none; display:inline-block;">
                ตั้งรหัสผ่านใหม่
              </a>
            </p>
            <p>หากปุ่มด้านบนใช้งานไม่ได้ ให้คัดลอกลิงก์นี้ไปวางในเบราว์เซอร์ของคุณ:<br />
              <span style="word-break:break-all;">{encodedLink}</span>
            </p>
            <p>หากคุณไม่ได้เป็นผู้ขอตั้งรหัสผ่านใหม่ กรุณาเพิกเฉยต่ออีเมลฉบับนี้ — รหัสผ่านของคุณจะไม่ถูกเปลี่ยนแปลงจนกว่าจะมีการกดลิงก์นี้และตั้งรหัสผ่านใหม่จริง หากลิงก์นี้เก่ากว่าคำขอล่าสุดของคุณ ลิงก์นี้จะใช้งานไม่ได้แล้วเช่นกัน</p>
            """;

        return EmailTemplateRenderer.RenderLayout("ตั้งรหัสผ่านใหม่", content);
    }
}
