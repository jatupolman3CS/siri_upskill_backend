namespace Siri.Modules.Live.Application;

/// <summary>
/// Masks an e-mail address for display to someone who has no business seeing it in full (an instructor looking at their class roster —
/// docs/contracts/P11-05-live-learner-instructor-api-join-gate.md section 4.4). The shape is <c>a***@g***.com</c>: the first character of
/// the local part and of the domain's first label, then <c>***</c>, then only the last domain label. Pure and static.
/// </summary>
public static class EmailMasker
{
    private const string Stars = "***";

    /// <summary>
    /// <c>alice@gmail.com</c> becomes <c>a***@g***.com</c>; <c>bob@mail.example.co.th</c> becomes <c>b***@m***.th</c>. A blank value gives
    /// <c>null</c>; a value that is not a plausible address (no <c>@</c>, or nothing before/after it) gives just <c>***</c> so no part of it is echoed.
    /// </summary>
    public static string? Mask(string? email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return null;
        }

        var value = email.Trim();
        var at = value.LastIndexOf('@');
        if (at <= 0 || at == value.Length - 1)
        {
            return Stars;
        }

        var local = value[..at];
        var domain = value[(at + 1)..];

        var lastDot = domain.LastIndexOf('.');
        var maskedDomain = lastDot <= 0 || lastDot == domain.Length - 1
            // A single-label host (no usable top-level label) is masked as a whole.
            ? FirstCharacter(domain) + Stars
            : FirstCharacter(domain) + Stars + domain[lastDot..];

        return FirstCharacter(local) + Stars + "@" + maskedDomain;
    }

    /// <summary>The first user-perceived character as a string, so a surrogate pair or a Thai combining sequence is never cut in half.</summary>
    private static string FirstCharacter(string value)
    {
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(value);
        return enumerator.MoveNext() ? (string)enumerator.Current : string.Empty;
    }
}
