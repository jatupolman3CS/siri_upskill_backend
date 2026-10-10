using System.Text;

namespace Siri.Modules.Catalog.Features;

/// <summary>
/// Turns the file name a browser sent into one that is safe to store, show and put in a
/// <c>Content-Disposition</c> header. The name is display-only — the storage key never contains it — but it
/// is still attacker-controlled text that ends up in HTML lists and HTTP headers.
/// </summary>
public static class AttachmentFileNames
{
    public const int MaxLength = 255;

    /// <summary>
    /// Returns the sanitized name, or <c>null</c> when nothing usable is left. Strips any directory part
    /// (both <c>/</c> and <c>\</c>, since browsers on Windows send either), control characters and the
    /// characters Windows forbids; collapses whitespace; truncates the base name — never the extension —
    /// to <see cref="MaxLength"/>.
    /// </summary>
    public static string? Sanitize(string? rawFileName)
    {
        if (string.IsNullOrWhiteSpace(rawFileName))
        {
            return null;
        }

        var name = rawFileName.Replace('\\', '/');
        var lastSeparator = name.LastIndexOf('/');
        if (lastSeparator >= 0)
        {
            name = name[(lastSeparator + 1)..];
        }

        var builder = new StringBuilder(name.Length);
        foreach (var ch in name)
        {
            if (char.IsControl(ch) || ch is '<' or '>' or ':' or '"' or '|' or '?' or '*')
            {
                continue;
            }

            builder.Append(ch);
        }

        name = string.Join(' ', builder.ToString().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)).Trim().Trim('.');

        if (name.Length == 0)
        {
            return null;
        }

        if (name.Length > MaxLength)
        {
            var extension = AttachmentFileValidator.GetFileExtension(name);
            var keepFromBase = Math.Max(1, MaxLength - extension.Length);
            name = name[..keepFromBase] + extension;
        }

        return name;
    }
}
