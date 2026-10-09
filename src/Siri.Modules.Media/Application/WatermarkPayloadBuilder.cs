using System.Globalization;
using System.Text;

namespace Siri.Modules.Media.Application;

/// <summary>
/// Builds the text the player overlays on the video as a dynamic watermark (SE-02, <c>docs/DECISIONS.md</c> Q8 /
/// <c>P2-22</c>). Q8 makes this the primary anti-piracy control — a leaked recording must identify the viewer
/// (name + email + timestamp) — so the payload is built here on the server only; the client just renders the string
/// (<c>security.md</c>: "watermark payload (ชื่อ/อีเมล) ต้องมาจาก server ห้ามให้ client กำหนดเอง").
/// <para>
/// Layout: <c>{displayName} · {email} · {yyyy-MM-dd HH:mm:ss} UTC</c>. The payload must always carry a stable
/// identifier of the viewer and must never invent an identity, so the degraded shapes are:
/// </para>
/// <list type="bullet">
/// <item>name + email → <c>Jane Doe · jane@example.com · 2026-10-09 10:00:00 UTC</c></item>
/// <item>email only → <c>jane@example.com · 2026-10-09 10:00:00 UTC</c> (the email is already unique)</item>
/// <item>name only → <c>Jane Doe · {userId} · 2026-10-09 10:00:00 UTC</c> (a name alone is not unique, so the user id GUID is added)</item>
/// <item>neither → <c>SIRI UpSkill · {userId} · 2026-10-09 10:00:00 UTC</c> (the form that existed before this change)</item>
/// <item>guest (free preview, no account) → <c>SIRI UpSkill · Guest · 2026-10-09 10:00:00 UTC</c></item>
/// </list>
/// <para>
/// The payload is rendered on screen and may end up in logs / CSV exports, so name and email are sanitised (control
/// characters, line/paragraph separators, bidi-override characters and the <c>·</c> field separator itself are replaced
/// by a space so a display name cannot forge or reorder fields) and the whole payload is capped at
/// <see cref="MaxLength"/> characters. When it does not fit, the timestamp and the identifier (email or user id)
/// are kept intact and the display name is shortened first.
/// </para>
/// </summary>
public static class WatermarkPayloadBuilder
{
    /// <summary>Hard cap on the payload length, in UTF-16 chars.</summary>
    public const int MaxLength = 120;

    /// <summary>Neutral label used for viewers without an account (free preview). Never a real or invented identity.</summary>
    public const string GuestLabel = "Guest";

    private const string BrandPrefix = "SIRI UpSkill";
    private const string Separator = " · ";
    private const char SeparatorCharacter = '·';
    private const string TimestampFormat = "yyyy-MM-dd HH:mm:ss";
    private const char Ellipsis = '…';

    /// <summary>Payload for a viewer without an account — a fixed label plus the timestamp, nothing else.</summary>
    public static string ForGuest(DateTime utcNow) =>
        $"{BrandPrefix}{Separator}{GuestLabel}{Separator}{FormatTimestamp(utcNow)}";

    /// <summary>
    /// Payload for an authenticated viewer. <paramref name="displayName"/> and <paramref name="email"/> may be
    /// <c>null</c>/blank (lookup failed, field empty) — see the class remarks for the fallback shapes.
    /// </summary>
    public static string ForUser(Guid userId, string? displayName, string? email, DateTime utcNow)
    {
        var name = Sanitize(displayName);
        var mail = Sanitize(email);
        var suffix = Separator + FormatTimestamp(utcNow);

        if (name.Length == 0 && mail.Length == 0)
        {
            return $"{BrandPrefix}{Separator}{userId}{suffix}";
        }

        var identifierBudget = MaxLength - suffix.Length;
        var identifier = Truncate(mail.Length > 0 ? mail : userId.ToString(), identifierBudget);

        var nameBudget = identifierBudget - identifier.Length - Separator.Length;
        if (name.Length == 0 || nameBudget < 1)
        {
            return identifier + suffix;
        }

        return Truncate(name, nameBudget) + Separator + identifier + suffix;
    }

    private static string FormatTimestamp(DateTime utcNow) =>
        // Invariant culture on purpose: with a th-TH current culture "yyyy" would print the Buddhist-era year (2569).
        utcNow.ToString(TimestampFormat, CultureInfo.InvariantCulture) + " UTC";

    /// <summary>Replaces anything that must not appear in an on-screen / logged field with a space, then collapses
    /// whitespace runs and trims.</summary>
    private static string Sanitize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasSpace = true; // swallows leading whitespace

        foreach (var c in value)
        {
            var isSpace = char.IsWhiteSpace(c) || IsDisallowed(c);
            if (isSpace)
            {
                if (!previousWasSpace)
                {
                    builder.Append(' ');
                    previousWasSpace = true;
                }

                continue;
            }

            builder.Append(c);
            previousWasSpace = false;
        }

        return builder.ToString().TrimEnd();
    }

    private static bool IsDisallowed(char c) =>
        char.IsControl(c)
        || c == SeparatorCharacter
        // Code points are numeric on purpose: a literal U+2028/U+2029 inside C# source would be read as a line terminator.
        || (int)c is 0x2028 or 0x2029                    // line / paragraph separator
        || (int)c is 0x200E or 0x200F                    // LRM / RLM
        || (int)c is >= 0x202A and <= 0x202E             // bidi embedding / override
        || (int)c is >= 0x2066 and <= 0x2069;            // bidi isolates

    private static string Truncate(string value, int maxLength)
    {
        if (value.Length <= maxLength)
        {
            return value;
        }

        if (maxLength <= 1)
        {
            return value[..Math.Max(maxLength, 0)];
        }

        var cut = maxLength - 1; // room for the ellipsis
        if (char.IsHighSurrogate(value[cut - 1]))
        {
            cut--; // never leave half of a surrogate pair behind
        }

        return value[..cut] + Ellipsis;
    }
}
