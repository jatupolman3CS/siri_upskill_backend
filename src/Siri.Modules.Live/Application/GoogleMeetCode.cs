using System.Text.RegularExpressions;

namespace Siri.Modules.Live.Application;

/// <summary>
/// The Meet "meeting code" (<c>abc-defg-hij</c>) of a room, taken from the room link (P11-13 contract section 4): the last path segment of
/// <c>https://meet.google.com/abc-defg-hij</c>, accepted only when it has exactly the Meet shape. The link is a capability URL and the code is the key into
/// Google's records, so <b>neither is ever logged</b>.
/// </summary>
public static partial class GoogleMeetCode
{
    /// <summary>Stand-in code used with the fake providers when <c>Live:Provider=Logging</c> (their room links are not Meet links).</summary>
    public const string DevCode = "abc-defg-hij";

    [GeneratedRegex("^[a-z]{3}-[a-z]{4}-[a-z]{3}$", RegexOptions.CultureInvariant, matchTimeoutMilliseconds: 100)]
    private static partial Regex CodePattern();

    /// <summary>The code of <paramref name="roomUrl"/>, or <c>null</c> when it is not an absolute URL whose last path segment looks like a Meet code.</summary>
    public static string? TryParse(string? roomUrl)
    {
        if (string.IsNullOrWhiteSpace(roomUrl) || !Uri.TryCreate(roomUrl.Trim(), UriKind.Absolute, out var uri))
        {
            return null;
        }

        var segment = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries).LastOrDefault();
        return segment is not null && IsValid(segment) ? segment : null;
    }

    /// <summary>True for exactly three lower-case letters, a dash, four letters, a dash, three letters.</summary>
    public static bool IsValid(string? code) => code is not null && code.Length == 12 && CodePattern().IsMatch(code);
}
