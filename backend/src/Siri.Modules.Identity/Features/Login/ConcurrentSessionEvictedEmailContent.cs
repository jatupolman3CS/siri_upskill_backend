using System.Net;
using Siri.Modules.Notification.Contracts;

namespace Siri.Modules.Identity.Features.Login;

/// <summary>
/// "You were signed out because of a login on another device" copy for SE-03 concurrent-session
/// eviction. Builds on top of the Notification module's shared HTML shell (<see cref="EmailTemplateRenderer"/>)
/// instead of hand-rolling a second layout — exactly the same shape as <c>Register/RegisterEmailContent.cs</c>
/// (task instruction: reuse <c>EmailLayout</c>, do not build a new layout mechanism).
/// </summary>
internal static class ConcurrentSessionEvictedEmailContent
{
    /// <summary>Matches <c>EmailOutboxMessage.TemplateKey</c> for messages built from this template.</summary>
    public const string TemplateKey = "identity-concurrent-session-evicted";

    public const string Subject = "คุณถูกออกจากระบบเนื่องจากเข้าสู่ระบบจากอุปกรณ์อื่น - SIRI UpSkill";

    /// <summary>
    /// <paramref name="displayName"/> is plain text and gets HTML-encoded (untrusted — a user's own
    /// display name). <paramref name="effectiveLimit"/> is the account's actual concurrent-session
    /// limit at the moment of eviction (system default or per-account override — whichever applied),
    /// included so the message is concrete rather than a generic "something happened" notice.
    /// </summary>
    public static string Render(string displayName, int effectiveLimit)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);

        var encodedName = WebUtility.HtmlEncode(displayName);

        var content = $"""
            <p>สวัสดีคุณ {encodedName},</p>
            <p>ระบบตรวจพบการเข้าสู่ระบบจากอุปกรณ์ใหม่บนบัญชีของคุณ บัญชีนี้อนุญาตให้เข้าสู่ระบบพร้อมกันได้สูงสุด
               {effectiveLimit} อุปกรณ์ เซสชันที่เข้าสู่ระบบเก่าสุดของคุณจึงถูกออกจากระบบโดยอัตโนมัติเพื่อรักษาขีดจำกัดนี้</p>
            <p>หากเป็นคุณเองที่เข้าสู่ระบบจากอุปกรณ์ใหม่นี้ ไม่ต้องดำเนินการใด ๆ เพิ่มเติม</p>
            <p>หากคุณไม่ได้เป็นผู้เข้าสู่ระบบจากอุปกรณ์นี้ กรุณาเปลี่ยนรหัสผ่านของคุณทันทีเพื่อความปลอดภัยของบัญชี</p>
            """;

        return EmailTemplateRenderer.RenderLayout("แจ้งเตือนความปลอดภัยบัญชีของคุณ", content);
    }
}
