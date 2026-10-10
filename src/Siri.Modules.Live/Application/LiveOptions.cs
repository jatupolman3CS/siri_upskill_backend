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

    /// <summary>Recording settings (<c>Live:Recording</c>) — currently only the automatic Google Meet import (<c>Live:Recording:AutoImport</c>, P11-13).</summary>
    public LiveRecordingOptions Recording { get; set; } = new();

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

/// <summary><c>Live:Recording</c> — groups the recording-related settings so the section path (<c>Live:Recording:AutoImport</c>) matches P11-13 contract section 2.</summary>
public sealed class LiveRecordingOptions
{
    public LiveRecordingAutoImportOptions AutoImport { get; set; } = new();
}

/// <summary>
/// <c>Live:Recording:AutoImport</c> — the automatic import of a Google Meet recording into the platform as a lesson (P11-13 contract section 2).
/// The whole feature is behind <see cref="Enabled"/> (default <c>false</c>): the Google app is unverified for the Restricted
/// <c>drive.meet.readonly</c> scope, so the owner switches it on when a Google Workspace account exists. With it off no discovery runs, no
/// import row is written, no consent button is offered and every instructor sees only the manual upload path.
/// </summary>
public sealed class LiveRecordingAutoImportOptions
{
    public const int MaxSearchWindowHours = 72;

    /// <summary>Master switch. Off: no discovery, no import rows, no consent button, capability is always <c>Manual</c>.</summary>
    public bool Enabled { get; set; }

    /// <summary>The first search for the recording happens this long after the scheduled end of the class.</summary>
    public int FirstSearchDelayMinutes { get; set; } = 10;

    /// <summary>Keep looking this long after the scheduled end; after that the import ends as <c>NoRecording</c> (the instructor uploads by hand).</summary>
    public int SearchWindowHours { get; set; } = 12;

    /// <summary>Consecutive transient failures (network, 5xx, 429) tolerated before the import is <c>Failed</c>.</summary>
    public int MaxAttempts { get; set; } = 6;

    /// <summary>Larger recordings are refused (<c>file_too_large</c>).</summary>
    public int MaxFileSizeMegabytes { get; set; } = 8192;

    /// <summary>A <c>Transferring</c> import not finished by then is reclaimed and restarted.</summary>
    public int TransferLeaseMinutes { get; set; } = 180;

    /// <summary>Imports processed per job run.</summary>
    public int BatchSize { get; set; } = 5;

    /// <summary><b>Development only</b> (<c>Live:Provider=Logging</c>): the file the fake recording provider serves. Must be empty in Production.</summary>
    public string DevSampleFilePath { get; set; } = string.Empty;

    /// <summary><see cref="MaxFileSizeMegabytes"/> in bytes.</summary>
    public long MaxFileSizeBytes => MaxFileSizeMegabytes * 1024L * 1024L;
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

        ValidateAutoImport(options.Recording.AutoImport, failures);

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }

    private static void ValidateAutoImport(LiveRecordingAutoImportOptions autoImport, List<string> failures)
    {
        const string prefix = $"{LiveOptions.SectionName}:Recording:AutoImport";

        void Positive(string name, int value)
        {
            if (value <= 0)
            {
                failures.Add($"{prefix}:{name} must be greater than zero.");
            }
        }

        Positive(nameof(LiveRecordingAutoImportOptions.FirstSearchDelayMinutes), autoImport.FirstSearchDelayMinutes);
        Positive(nameof(LiveRecordingAutoImportOptions.SearchWindowHours), autoImport.SearchWindowHours);
        Positive(nameof(LiveRecordingAutoImportOptions.MaxAttempts), autoImport.MaxAttempts);
        Positive(nameof(LiveRecordingAutoImportOptions.MaxFileSizeMegabytes), autoImport.MaxFileSizeMegabytes);
        Positive(nameof(LiveRecordingAutoImportOptions.TransferLeaseMinutes), autoImport.TransferLeaseMinutes);
        Positive(nameof(LiveRecordingAutoImportOptions.BatchSize), autoImport.BatchSize);

        if (autoImport.SearchWindowHours > LiveRecordingAutoImportOptions.MaxSearchWindowHours)
        {
            failures.Add($"{prefix}:{nameof(LiveRecordingAutoImportOptions.SearchWindowHours)} must be at most {LiveRecordingAutoImportOptions.MaxSearchWindowHours} hours.");
        }
    }
}
