using System.Net;
using Siri.Modules.Notification.Contracts;

namespace Siri.Modules.Identity.Features.ResetPassword;

/// <summary>
/// "Your password was just changed" security-notification copy, sent after a successful reset — see
/// <c>Handler.cs</c>'s doc comment for the decision to send this at all. Builds on top of the
/// Notification module's shared HTML shell (<see cref="EmailTemplateRenderer"/>), same shape as
/// <c>Login/ConcurrentSessionEvictedEmailContent.cs</c> (also a security-notification email, not a
/// link-to-click one).
/// </summary>
internal static class PasswordChangedEmailContent
{
    /// <summary>Matches <c>EmailOutboxMessage.TemplateKey</c> for messages built from this template.</summary>
    public const string TemplateKey = "identity-password-changed";

    public const string Subject = "รหัสผ่านของคุณถูกเปลี่ยนแล้ว - SIRI UpSkill";

    /// <summary><paramref name="displayName"/> is plain text and gets HTML-encoded (untrusted — a
    /// user's own display name).</summary>
    public static string Render(string displayName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var encodedName = WebUtility.HtmlEncode(displayName);

        var content = $"""
            <p>สวัสดีคุณ {encodedName},</p>
            <p>รหัสผ่านสำหรับบัญชี SIRI UpSkill ของคุณถูกเปลี่ยนเรียบร้อยแล้วเมื่อครู่นี้ เพื่อความปลอดภัย
               ระบบได้ออกจากระบบทุกอุปกรณ์ที่เคยเข้าสู่ระบบไว้ก่อนหน้านี้แล้ว คุณจะต้องเข้าสู่ระบบใหม่ด้วยรหัสผ่านใหม่บนทุกอุปกรณ์</p>
            <p>หากเป็นคุณเองที่ทำรายการนี้ ไม่ต้องดำเนินการใด ๆ เพิ่มเติม</p>
            <p>หากคุณไม่ได้เป็นผู้ทำรายการนี้ บัญชีของคุณอาจถูกบุกรุก กรุณาติดต่อทีมงานทันทีและตั้งรหัสผ่านใหม่อีกครั้งผ่านลิงก์ "ลืมรหัสผ่าน"</p>
            """;

        return EmailTemplateRenderer.RenderLayout("แจ้งเตือนความปลอดภัยบัญชีของคุณ", content);
    }
}
