using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Text;
using System.Text.RegularExpressions;

namespace Siri.SharedKernel;

/// <summary>
/// Detects — and removes — links to online-meeting rooms (Google Meet / Zoom / Teams) inside <b>free text</b> an instructor typed: a live
/// session's title, description or cancel reason. A room URL is a capability: the platform hands it out only through the join gate
/// (enrollment + time window + audit), so it must never be reachable through a field that is shown publicly, mailed to every learner or
/// written to a calendar file. Lives in the SharedKernel so Catalog (write-side validation, public read model) and Live (outbound
/// messages, learner queries) apply <em>the same</em> rule without Catalog referencing Live.
/// <para>
/// What counts as a link: anything that looks like a URL (or a bare host path) on one of the meeting hosts — with or without a scheme, any
/// letter case, any sub-domain (<c>us02web.zoom.us</c>) — <em>including disguised forms</em>: invisible/zero-width characters or line breaks
/// inside the host (browsers' URL parsers drop tabs/newlines), full-width letters and dots (IDNA maps them to ASCII), and percent-/HTML-encoded
/// text. A look-alike registrable domain (<c>evilzoom.us</c>, <c>zoom.us.evil.test</c> as a suffix of another host) is <b>not</b> a meeting host.
/// Any other <c>https://</c> link (an article, a slide deck) is left alone.
/// </para>
/// <para>
/// The host list is the default allow-list of the Live module (<c>Live:AllowedMeetingHosts</c> may add more for callers that know the
/// configuration; Catalog only knows the defaults).
/// </para>
/// </summary>
public static class MeetingLinkText
{
    /// <summary>What a removed link is replaced with.</summary>
    public const string Placeholder = "[ลิงก์ห้องประชุม]";

    /// <summary>Stable validation/error reason when a field that must not carry a room link does.</summary>
    public const string ContainsLinkReason = "live.session_text_contains_meeting_link";

    /// <summary>Hosts (and their sub-domains) that are always treated as meeting rooms.</summary>
    public static readonly IReadOnlyList<string> DefaultHosts =
        ["meet.google.com", "zoom.us", "teams.microsoft.com", "teams.live.com"];

    private static readonly ConcurrentDictionary<string, Regex> PatternCache = new(StringComparer.Ordinal);

    /// <summary><c>true</c> when <paramref name="text"/> contains a link to a meeting host in any of the forms described on the type.
    /// Fails closed: text the matcher cannot finish in time counts as containing one.</summary>
    /// <param name="text">Free text (may be <c>null</c>).</param>
    /// <param name="extraHosts">Additional configured hosts; the defaults are always included.</param>
    public static bool ContainsLink(string? text, IEnumerable<string>? extraHosts = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return false;
        }

        var pattern = BuildPattern(extraHosts);
        try
        {
            return Variants(text).Any(variant => pattern.IsMatch(variant));
        }
        catch (RegexMatchTimeoutException)
        {
            return true;
        }
    }

    /// <summary>Returns <paramref name="text"/> with every link to a meeting host replaced by <see cref="Placeholder"/>. Plain text is returned
    /// unchanged; text that hid its link behind invisible characters / encoding is returned in its decoded form with the link replaced (the
    /// hidden form could not be replaced faithfully).</summary>
    /// <param name="text">Free text (may be <c>null</c>).</param>
    /// <param name="extraHosts">Additional configured hosts; the defaults are always included.</param>
    public static string? Scrub(string? text, IEnumerable<string>? extraHosts = null)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }

        var pattern = BuildPattern(extraHosts);
        try
        {
            // One pass removes the links visible in the first variant that has any; a text can hold several links hidden in different ways (a plain one and a
            // zero-width-disguised one), so rescan what is left until nothing matches in any variant — never a half-scrubbed result, and Scrub(Scrub(x)) == Scrub(x).
            var current = text;
            for (var pass = 0; pass < MaxScrubPasses; pass++)
            {
                var next = ScrubOnce(current, pattern);
                if (ReferenceEquals(next, current))
                {
                    return current;
                }

                current = next;
            }

            // Still changing after that many passes: not a text a person wrote. Fail closed.
            return Placeholder;
        }
        catch (RegexMatchTimeoutException)
        {
            return Placeholder;
        }
    }

    /// <summary>Upper bound on rescans in <see cref="Scrub"/>; every pass removes at least one link, so real text needs two or three.</summary>
    private const int MaxScrubPasses = 16;

    /// <summary>Replaces the links in the first variant of <paramref name="text"/> that contains any; returns the same instance when no variant does.</summary>
    private static string ScrubOnce(string text, Regex pattern)
    {
        foreach (var variant in Variants(text))
        {
            if (pattern.IsMatch(variant))
            {
                return pattern.Replace(variant, Placeholder);
            }
        }

        return text;
    }

    /// <summary>The same as <see cref="Scrub"/> for a value that must not become <c>null</c>.</summary>
    public static string ScrubRequired(string text, IEnumerable<string>? extraHosts = null) =>
        Scrub(text, extraHosts) ?? string.Empty;

    /// <summary>The text itself, then progressively "de-obfuscated" forms of it. Matching any one of them is a match — a match can only be
    /// added by a variant, never lost, so plain text that already matched keeps matching exactly as before.</summary>
    private static IEnumerable<string> Variants(string text)
    {
        yield return text;

        // 1. Invisible characters removed and full-width forms mapped to ASCII: "meet.go<ZWSP>ogle.com", "meet<U+3002>google<U+3002>com" (and the full-width letters).
        var cleaned = StripInvisibleAndMapWidth(text);
        if (!ReferenceEquals(cleaned, text) && !string.Equals(cleaned, text, StringComparison.Ordinal))
        {
            yield return cleaned;
        }

        // 2. Line breaks / tabs inside a host: URL parsers delete them, so "meet.go<TAB>ogle.com" opens as meet.google.com.
        var joined = RemoveLineBreaks(cleaned);
        if (!string.Equals(joined, cleaned, StringComparison.Ordinal))
        {
            yield return joined;
        }

        // 3. Percent-/HTML-encoded: "https%3A%2F%2Fmeet%2Egoogle%2Ecom%2Fabc", "meet&#46;google&#46;com".
        var decoded = Decode(joined);
        if (!string.Equals(decoded, joined, StringComparison.Ordinal))
        {
            yield return decoded;
        }
    }

    private static string StripInvisibleAndMapWidth(string text)
    {
        StringBuilder? builder = null;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            char? mapped = c;

            if (IsInvisible(c))
            {
                mapped = null;
            }
            else if (c is '\u3002' or '\uFF0E' or '\uFF61')
            {
                mapped = '.'; // ideographic / full-width / half-width ideographic full stop — IDNA treats all of them as a dot
            }
            else if (c is >= '\uFF01' and <= '\uFF5E')
            {
                mapped = (char)(c - 0xFEE0); // full-width ASCII block -> ASCII
            }

            if (mapped == c)
            {
                builder?.Append(c);
                continue;
            }

            builder ??= new StringBuilder(text.Length).Append(text, 0, i);
            if (mapped is { } replacement)
            {
                builder.Append(replacement);
            }
        }

        return builder?.ToString() ?? text;
    }

    /// <summary>Format characters (zero-width space/joiners, BOM, soft hyphen, bidi marks, word joiner) and the "default ignorable" fillers.</summary>
    private static bool IsInvisible(char c) =>
        char.GetUnicodeCategory(c) == UnicodeCategory.Format
        || c is '\u034F' or '\u115F' or '\u1160' or '\u3164' or '\uFFA0'
        || c is >= '\uFE00' and <= '\uFE0F';

    private static string RemoveLineBreaks(string text)
    {
        if (text.AsSpan().IndexOfAny("\t\r\n\v\f\u0085\u2028\u2029") < 0)
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var c in text)
        {
            if (c is not ('\t' or '\r' or '\n' or '\v' or '\f' or '\u0085' or '\u2028' or '\u2029'))
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }

    private static string Decode(string text)
    {
        if (text.IndexOf('%', StringComparison.Ordinal) < 0 && text.IndexOf('&', StringComparison.Ordinal) < 0)
        {
            return text;
        }

        // Uri.UnescapeDataString leaves invalid escape sequences as they are (it does not throw on them).
        return WebUtility.HtmlDecode(Uri.UnescapeDataString(text));
    }

    private static Regex BuildPattern(IEnumerable<string>? extraHosts)
    {
        var hosts = DefaultHosts
            .Concat(extraHosts ?? [])
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .Select(host => host.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        return PatternCache.GetOrAdd(string.Join('|', hosts), key =>
        {
            // [optional scheme][optional sub-domains.]host[path/query/fragment up to whitespace or a closing bracket/quote].
            // The look-behind keeps "evilzoom.us" from matching as "zoom.us"; sub-domain labels are consumed by the group so
            // "us02web.zoom.us" is removed whole.
            var alternatives = string.Join('|', key.Split('|').Select(Regex.Escape));
            var pattern = $@"(?<![a-z0-9.\-])(?:https?://)?(?:[a-z0-9\-]+\.)*(?:{alternatives})(?![a-z0-9\-])(?:[/?#][^\s<>""')\]]*)?";
            return new Regex(pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(250));
        });
    }
}
