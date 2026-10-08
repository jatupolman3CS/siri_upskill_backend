using System.ComponentModel.DataAnnotations;
using System.Net.Mail;
using Microsoft.Extensions.Options;

namespace Siri.Modules.Live.Application;

/// <summary>How the Live module obtains online rooms (<c>Live:Provider</c>).</summary>
public enum LiveProviderMode
{
    /// <summary>Use Google Calendar/Meet when the instructor connected Google; otherwise the instructor pastes a link.</summary>
    GoogleMeet,

    /// <summary>Never call Google; every room is a manually pasted link.</summary>
    ManualOnly,

    /// <summary><b>Development only.</b> Fake OAuth + fake Meet URLs (<c>https://meet.invalid/dev/...</c>). Never a default;
    /// <c>ProductionConfigurationGuard</c> refuses it in Production.</summary>
    Logging,
}

/// <summary>
/// Bound from configuration section <see cref="SectionName"/> (<c>Live</c>) — P11-03 contract section 3.5. Options pattern +
/// <c>ValidateOnStart()</c>. No secrets live here (the Google client secret is in <c>Integrations:Google</c>).
/// <para>
/// Array defaults are intentionally empty: the configuration binder APPENDS to a pre-populated array instead of replacing it,
/// which would silently duplicate defaults — read <see cref="GetEffectiveAllowedMeetingHosts"/>.
/// </para>
/// </summary>
public sealed class LiveOptions
{
    public const string SectionName = "Live";

    /// <summary>Where <see cref="PublicBaseUrl"/> comes from when it is not set explicitly.</summary>
    public const string FallbackPublicBaseUrlKey = "Seo:PublicBaseUrl";

    /// <summary>Hosts (and their sub-domains) an instructor-supplied meeting link may point at.</summary>
    public static readonly IReadOnlyList<string> DefaultAllowedMeetingHosts = Siri.SharedKernel.MeetingLinkText.DefaultHosts;

    public LiveProviderMode Provider { get; set; } = LiveProviderMode.GoogleMeet;

    /// <summary>Origin of the frontend: used to build <c>.../live/{sessionId}/join</c> links, the post-OAuth redirect and the
    /// host of ICS UIDs. Defaults to <c>Seo:PublicBaseUrl</c>. Absolute https (http only for loopback).</summary>
    public string PublicBaseUrl { get; set; } = string.Empty;

    /// <summary>Minutes before a session starts at which learners may enter the room (join gate and display state).</summary>
    [Range(5, 120)]
    public int JoinWindowBeforeMinutes { get; set; } = 15;

    /// <summary>How far ahead (days) invitations are created. Used by P11-04.</summary>
    [Range(1, 730)]
    public int InviteLookaheadDays { get; set; } = 180;

    /// <summary>Maximum learners added as Google Calendar attendees per session. Used by P11-04. Google caps an event at ~200.</summary>
    [Range(1, 190)]
    public int GoogleAttendeeCap { get; set; } = 150;

    /// <summary>Allowed meeting-link hosts. Empty = <see cref="DefaultAllowedMeetingHosts"/>.</summary>
    public string[] AllowedMeetingHosts { get; set; } = [];

    /// <summary>ICS <c>ORGANIZER</c> address. Defaults to <c>no-reply@{host of PublicBaseUrl}</c>. Used by P11-04.</summary>
    public string OrganizerEmail { get; set; } = string.Empty;

    /// <summary>Configured allowed hosts (trimmed, lower-cased, distinct) or the defaults when none are configured.</summary>
    public IReadOnlyList<string> GetEffectiveAllowedMeetingHosts()
    {
        var configured = AllowedMeetingHosts
            .Where(host => !string.IsNullOrWhiteSpace(host))
            .Select(host => host.Trim().ToLowerInvariant())
            .Distinct(StringComparer.Ordinal)
            .ToArray();

        return configured.Length == 0 ? DefaultAllowedMeetingHosts : configured;
    }

    /// <summary><see cref="PublicBaseUrl"/> without a trailing slash (empty if unset).</summary>
    public string GetNormalizedPublicBaseUrl() => PublicBaseUrl.Trim().TrimEnd('/');

    /// <summary>Fills the values that default from other configuration: <c>PublicBaseUrl</c> from
    /// <paramref name="fallbackPublicBaseUrl"/> and <c>OrganizerEmail</c> from the resulting host.</summary>
    public static void ApplyDefaults(LiveOptions options, string? fallbackPublicBaseUrl)
    {
        ArgumentNullException.ThrowIfNull(options);

        if (string.IsNullOrWhiteSpace(options.PublicBaseUrl))
        {
            options.PublicBaseUrl = fallbackPublicBaseUrl?.Trim() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(options.OrganizerEmail)
            && Uri.TryCreate(options.PublicBaseUrl, UriKind.Absolute, out var baseUri)
            && !string.IsNullOrEmpty(baseUri.IdnHost))
        {
            options.OrganizerEmail = $"no-reply@{baseUri.IdnHost}";
        }
    }

    /// <summary>True for an absolute https URL, or http for a loopback host (local development).</summary>
    public static bool IsAcceptableOrigin(string? value)
    {
        if (string.IsNullOrWhiteSpace(value) || !Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri))
        {
            return false;
        }

        return uri.Scheme == Uri.UriSchemeHttps || (uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback);
    }
}

/// <summary>Cross-field validation for <see cref="LiveOptions"/> that data annotations cannot express.</summary>
public sealed class LiveOptionsValidator : IValidateOptions<LiveOptions>
{
    public ValidateOptionsResult Validate(string? name, LiveOptions options)
    {
        var failures = new List<string>();

        if (!LiveOptions.IsAcceptableOrigin(options.PublicBaseUrl))
        {
            failures.Add(
                $"{LiveOptions.SectionName}:{nameof(LiveOptions.PublicBaseUrl)} (or {LiveOptions.FallbackPublicBaseUrlKey}) must be an absolute https URL (http only for loopback).");
        }

        foreach (var host in options.AllowedMeetingHosts.Where(host => !string.IsNullOrWhiteSpace(host)))
        {
            // A bare host name: no scheme, port, path, wildcard or whitespace.
            if (Uri.CheckHostName(host.Trim()) != UriHostNameType.Dns || host.Contains('*', StringComparison.Ordinal))
            {
                failures.Add($"{LiveOptions.SectionName}:{nameof(LiveOptions.AllowedMeetingHosts)} contains '{host}', which is not a bare DNS host name.");
            }
        }

        if (!string.IsNullOrWhiteSpace(options.OrganizerEmail) && !MailAddress.TryCreate(options.OrganizerEmail, out _))
        {
            failures.Add($"{LiveOptions.SectionName}:{nameof(LiveOptions.OrganizerEmail)} is not a valid e-mail address.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
