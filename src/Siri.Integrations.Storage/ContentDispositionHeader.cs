using System.Text;

namespace Siri.Integrations.Storage;

/// <summary>
/// Builds a <c>Content-Disposition: attachment</c> header value for an arbitrary (possibly Thai) file
/// name. HTTP headers are ASCII, so the value carries an ASCII-only <c>filename</c> fallback plus the real
/// name as RFC 5987/6266 <c>filename*=UTF-8''…</c>; every current browser prefers the latter.
/// </summary>
internal static class ContentDispositionHeader
{
    private const int MaxFallbackLength = 100;

    public static string BuildAttachment(string fileName)
    {
        var name = string.IsNullOrWhiteSpace(fileName) ? "download" : fileName.Trim();

        return $"attachment; filename=\"{AsciiFallback(name)}\"; filename*=UTF-8''{Uri.EscapeDataString(name)}";
    }

    private static string AsciiFallback(string name)
    {
        var builder = new StringBuilder(Math.Min(name.Length, MaxFallbackLength));

        foreach (var ch in name)
        {
            if (builder.Length >= MaxFallbackLength)
            {
                break;
            }

            // Printable ASCII only; quote and backslash would break out of the quoted-string.
            builder.Append(ch is >= ' ' and <= '~' and not '"' and not '\\' ? ch : '_');
        }

        return builder.Length == 0 ? "download" : builder.ToString();
    }
}
