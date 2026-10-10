using System.Text.RegularExpressions;
using Microsoft.Extensions.Options;

namespace Siri.Integrations.Google;

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> (<c>Integrations:Google</c>). Options pattern +
/// <c>ValidateOnStart()</c> (P11-03 contract section 3.5).
/// <para>
/// <b>Nothing is faked.</b> <see cref="ClientId"/>/<see cref="ClientSecret"/>/<see cref="RedirectUri"/> default to
/// empty: an empty <see cref="ClientId"/> means "the connect-Google feature is switched off"
/// (<see cref="IsConfigured"/> == false) and the rest of the system keeps working with manual meeting links.
/// There is no placeholder default and no mock credential.
/// </para>
/// <para>
/// <see cref="ClientSecret"/> is a secret: <c>dotnet user-secrets</c> (dev) / environment (prod), set for BOTH
/// <c>Siri.Api</c> and <c>Siri.Workers</c>. It is never logged and never leaves this process.
/// </para>
/// </summary>
public sealed class GoogleOAuthOptions
{
    public const string SectionName = "Integrations:Google";

    public const string DefaultPostConnectRedirectPath = "/instructor/live-settings";

    public const int DefaultHttpTimeoutSeconds = 15;

    // Same shape the connect endpoint applies to a caller-supplied returnPath: stays inside the instructor area
    // of the SPA (no scheme/host, no "//", no backslash, no "..").
    private static readonly Regex InstructorPathPattern =
        new(@"^/instructor/[A-Za-z0-9/_\-]*\z", RegexOptions.Compiled | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));

    /// <summary>OAuth 2.0 Web-application client id. Empty = feature off.</summary>
    public string ClientId { get; set; } = string.Empty;

    /// <summary>OAuth client secret. <b>SECRET.</b> Required whenever <see cref="ClientId"/> is set.</summary>
    public string ClientSecret { get; set; } = string.Empty;

    /// <summary>Authorized redirect URI registered in Google Cloud, exactly:
    /// <c>{API origin}/api/live/instructor/google/callback</c>.</summary>
    public string RedirectUri { get; set; } = string.Empty;

    /// <summary>
    /// Requested scopes. Left empty by default on purpose (the configuration binder APPENDS to a pre-populated
    /// array rather than replacing it, which would silently duplicate the defaults). Read
    /// <see cref="GetEffectiveScopes"/>, which falls back to <see cref="GoogleScopes.DefaultScopes"/>.
    /// </summary>
    public string[] Scopes { get; set; } = [];

    /// <summary>SPA path the callback redirects to after connecting (must start with <c>/instructor/</c>).</summary>
    public string PostConnectRedirectPath { get; set; } = DefaultPostConnectRedirectPath;

    public int HttpTimeoutSeconds { get; set; } = DefaultHttpTimeoutSeconds;

    /// <summary>
    /// <b>Development only</b> (read exclusively by the fake <c>LoggingGoogleOAuthService</c> that <c>Live:Provider=Logging</c> selects; the real service
    /// ignores it). The e-mail address the fake Google account reports. Empty = <c>dev-instructor@example.test</c> (a "personal" account). Set it to an
    /// address ending in <c>@workspace.example.test</c> to rehearse a Google Workspace account without a Workspace subscription.
    /// </summary>
    public string DevAccountEmail { get; set; } = string.Empty;

    /// <summary>True when the connect-Google feature is switched on (a client id is configured).</summary>
    public bool IsConfigured => !string.IsNullOrWhiteSpace(ClientId);

    /// <summary>The scopes to request: configured ones (trimmed, de-duplicated, order kept) or
    /// <see cref="GoogleScopes.DefaultScopes"/> when none are configured.</summary>
    public IReadOnlyList<string> GetEffectiveScopes()
    {
        var configured = Scopes
            .Where(scope => !string.IsNullOrWhiteSpace(scope))
            .Select(scope => scope.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return configured.Length == 0 ? GoogleScopes.DefaultScopes : configured;
    }

    /// <summary>True when <paramref name="path"/> is a safe in-SPA instructor path: starts with <c>/instructor/</c>,
    /// only <c>[A-Za-z0-9/_-]</c> after it, no <c>//</c>, no <c>..</c>, at most 200 characters. Single source of the rule
    /// for both <see cref="PostConnectRedirectPath"/> and a caller-supplied <c>returnPath</c>.</summary>
    public static bool IsSafeInstructorPath(string? path)
    {
        if (string.IsNullOrEmpty(path) || path.Length > 200)
        {
            return false;
        }

        return InstructorPathPattern.IsMatch(path)
            && !path.Contains("//", StringComparison.Ordinal)
            && !path.Contains("..", StringComparison.Ordinal);
    }

    /// <summary>True for an absolute https URL, or http only for a loopback host (local development).</summary>
    public static bool IsAcceptableRedirectUri(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value, UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }
}

/// <summary>
/// Cross-field validation for <see cref="GoogleOAuthOptions"/> (P11-03 contract section 3.5). An empty
/// <c>ClientId</c> passes (it simply switches the feature off), but a half-configured client (id without secret,
/// bad redirect URI, no calendar scope) fails the host start loudly instead of failing at the first instructor click.
/// </summary>
public sealed class GoogleOAuthOptionsValidator : IValidateOptions<GoogleOAuthOptions>
{
    public ValidateOptionsResult Validate(string? name, GoogleOAuthOptions options)
    {
        var failures = new List<string>();

        if (options.HttpTimeoutSeconds is < 1 or > 120)
        {
            failures.Add($"{GoogleOAuthOptions.SectionName}:{nameof(GoogleOAuthOptions.HttpTimeoutSeconds)} must be between 1 and 120.");
        }

        if (!GoogleOAuthOptions.IsSafeInstructorPath(options.PostConnectRedirectPath))
        {
            failures.Add($"{GoogleOAuthOptions.SectionName}:{nameof(GoogleOAuthOptions.PostConnectRedirectPath)} must be an in-app path starting with '/instructor/'.");
        }

        if (!string.IsNullOrEmpty(options.DevAccountEmail)
            && (options.DevAccountEmail.Length > 254 || options.DevAccountEmail.Count(c => c == '@') != 1 || options.DevAccountEmail.Any(char.IsWhiteSpace)))
        {
            failures.Add($"{GoogleOAuthOptions.SectionName}:{nameof(GoogleOAuthOptions.DevAccountEmail)} must be a single e-mail address (or empty).");
        }

        if (options.IsConfigured)
        {
            if (string.IsNullOrWhiteSpace(options.ClientSecret))
            {
                failures.Add($"{GoogleOAuthOptions.SectionName}:{nameof(GoogleOAuthOptions.ClientSecret)} is required when ClientId is set.");
            }

            if (!GoogleOAuthOptions.IsAcceptableRedirectUri(options.RedirectUri))
            {
                failures.Add($"{GoogleOAuthOptions.SectionName}:{nameof(GoogleOAuthOptions.RedirectUri)} must be an absolute https URL (http only for loopback) when ClientId is set.");
            }

            var scopes = options.GetEffectiveScopes();
            if (!scopes.Contains(GoogleScopes.OpenId, StringComparer.Ordinal)
                || !scopes.Contains(GoogleScopes.Email, StringComparer.Ordinal)
                || !GoogleScopes.HasCalendarScope(scopes))
            {
                failures.Add($"{GoogleOAuthOptions.SectionName}:{nameof(GoogleOAuthOptions.Scopes)} must contain 'openid', 'email' and at least one of calendar.events.owned / calendar.events / calendar.");
            }
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
