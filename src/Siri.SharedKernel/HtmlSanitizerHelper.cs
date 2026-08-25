using Ganss.Xss;

namespace Siri.SharedKernel;

/// <summary>
/// Server-side HTML sanitizer for rich content / blog posts (security.md: "HTML จาก CMS ต้อง sanitize
/// ที่ server ด้วย allowlist ก่อนเก็บ และก่อนแสดง").
/// <para>
/// Backed by <c>Ganss.Xss.HtmlSanitizer</c> — a real HTML-parsing (AngleSharp), allowlist-based
/// sanitizer, not a hand-rolled regex. A regex-based version of this class existed briefly and was
/// replaced after empirically confirming it was bypassable (e.g. <c>&lt;a href="jav&amp;#9;ascript:...">
/// </c> — a tab character inside the scheme name, which the HTML5 URL parsing algorithm strips before
/// browsers evaluate the scheme, so the link still executes as <c>javascript:</c> even though no
/// substring literally matching a "javascript:" pattern survives). Regexes cannot reliably parse HTML for
/// this purpose — there is no bounded set of "bad patterns" to block, only a safe set of tags/attributes
/// worth allowing, which is what an allowlist-based parser enforces structurally instead of textually.
/// </para>
/// </summary>
public static class HtmlSanitizerHelper
{
    private static readonly HtmlSanitizer Sanitizer = CreateSanitizer();

    public static string Sanitize(string? html)
    {
        return string.IsNullOrWhiteSpace(html) ? string.Empty : Sanitizer.Sanitize(html);
    }

    private static HtmlSanitizer CreateSanitizer()
    {
        var sanitizer = new HtmlSanitizer();

        // Reset to an explicit allowlist sized for blog/rich-text post content — deliberately narrower
        // than the library's own generous defaults (which include e.g. forms and a wider attribute set
        // than a marketing blog post needs).
        sanitizer.AllowedTags.Clear();
        foreach (var tag in new[]
                 {
                     "p", "br", "hr", "strong", "b", "em", "i", "u", "s", "sub", "sup", "span", "div",
                     "h1", "h2", "h3", "h4", "h5", "h6",
                     "ul", "ol", "li",
                     "a", "img",
                     "blockquote", "code", "pre",
                     "table", "thead", "tbody", "tr", "th", "td",
                 })
        {
            sanitizer.AllowedTags.Add(tag);
        }

        sanitizer.AllowedAttributes.Clear();
        foreach (var attribute in new[] { "href", "src", "alt", "title" })
        {
            sanitizer.AllowedAttributes.Add(attribute);
        }

        sanitizer.AllowedCssProperties.Clear();
        sanitizer.AllowedSchemes.Clear();
        foreach (var scheme in new[] { "http", "https", "mailto" })
        {
            sanitizer.AllowedSchemes.Add(scheme);
        }

        // KeepChildNodes: an author writing e.g. <table> content the allowlist doesn't recognize
        // (a stray <font> from pasted content) should lose the wrapper tag, not the text inside it —
        // matches editorial expectation better than silently deleting entire chunks of a post.
        sanitizer.KeepChildNodes = true;

        return sanitizer;
    }
}
