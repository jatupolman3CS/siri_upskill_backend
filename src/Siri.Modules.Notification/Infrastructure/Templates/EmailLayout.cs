using System.Globalization;

namespace Siri.Modules.Notification.Infrastructure.Templates;

/// <summary>
/// Shared HTML shell every outbound email renders inside: brand header/footer around a
/// <c>{{Content}}</c> placeholder for the message-specific body. Deliberately a small
/// string-templating helper, not a templating-engine dependency (Razor/Scriban/...) — the layout is
/// one fixed shell substituted once per email, so a real engine would be more machinery than this
/// problem needs (task P0-19: "do not over-engineer a full templating engine dependency").
/// <para>
/// <b>Font stack:</b> Thai web fonts (Sarabun, Prompt, ...) are almost never installed on a
/// recipient's own device, and most email clients strip or ignore <c>@font-face</c> / web-font
/// <c>&lt;link&gt;</c> tags entirely (Gmail, Outlook desktop, and many mobile mail apps all do this).
/// Assuming a custom Thai font renders is therefore not a safe bet. Instead this stack leans on each
/// OS's own built-in, Thai-capable system font first — Segoe UI / Leelawadee UI on Windows, the San
/// Francisco / Helvetica Neue family on Apple platforms (both ship Thai glyphs) — falling back to
/// Tahoma/Arial and finally a generic sans-serif for anything else.
/// </para>
/// </summary>
public static class EmailLayout
{
    private const string FontStack =
        "-apple-system, BlinkMacSystemFont, 'Segoe UI', 'Leelawadee UI', Tahoma, Arial, sans-serif";

    /// <summary>
    /// Wraps <paramref name="contentHtml"/> in the branded shell. <paramref name="contentHtml"/> is
    /// inserted as-is — callers are responsible for it already being safe HTML (same rule as CMS
    /// content: sanitize/encode untrusted text before it reaches here, not after).
    /// </summary>
    public static string Render(string title, string contentHtml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentNullException.ThrowIfNull(contentHtml);

        return Shell
            .Replace("{{FontStack}}", FontStack, StringComparison.Ordinal)
            .Replace("{{Title}}", title, StringComparison.Ordinal)
            .Replace("{{Content}}", contentHtml, StringComparison.Ordinal)
            .Replace("{{Year}}", DateTime.UtcNow.Year.ToString(CultureInfo.InvariantCulture), StringComparison.Ordinal);
    }

    // Plain (non-interpolated) raw string on purpose: {{Placeholder}} tokens below must survive
    // exactly as written for the .Replace() calls above — a $"""...""" interpolated raw string would
    // instead treat single braces as interpolation syntax and require escaping that defeats the point.
    private const string Shell = """
        <!doctype html>
        <html lang="th">
        <head>
        <meta charset="utf-8" />
        <meta name="viewport" content="width=device-width, initial-scale=1" />
        <title>{{Title}}</title>
        </head>
        <body style="margin:0; padding:0; background-color:#f4f4f5; font-family:{{FontStack}};">
          <table role="presentation" width="100%" cellpadding="0" cellspacing="0" style="background-color:#f4f4f5; padding:24px 0;">
            <tr>
              <td align="center">
                <table role="presentation" width="600" cellpadding="0" cellspacing="0" style="max-width:600px; width:100%; background-color:#ffffff; border-radius:8px; overflow:hidden;">
                  <tr>
                    <td style="background-color:#111827; padding:20px 32px;">
                      <span style="font-size:18px; font-weight:700; color:#ffffff;">SIRI UpSkill</span>
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:32px; color:#1f2937; font-size:15px; line-height:1.6;">
                      {{Content}}
                    </td>
                  </tr>
                  <tr>
                    <td style="padding:20px 32px; background-color:#f9fafb; color:#6b7280; font-size:12px; line-height:1.5;">
                      อีเมลนี้ส่งจากระบบ SIRI UpSkill กรุณาอย่าตอบกลับอีเมลฉบับนี้<br />
                      &copy; {{Year}} SIRI UpSkill. All rights reserved.
                    </td>
                  </tr>
                </table>
              </td>
            </tr>
          </table>
        </body>
        </html>
        """;
}
