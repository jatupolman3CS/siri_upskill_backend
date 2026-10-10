using Microsoft.Extensions.Configuration;
using Siri.Integrations.Google;
using Siri.Modules.Live.Application;

namespace Siri.Modules.Live;

/// <summary>
/// What a Production host must have configured for the Live module to be safe (P11-03 contract section 3.5). Shared by every process that loads the
/// module — the API's <c>ProductionConfigurationGuard</c> and the Workers host, which runs the sync job — so neither can start in Production
/// with fake rooms or a half-configured Google client.
/// <para>
/// Not a requirement: Google being configured at all. An empty <c>Integrations:Google:ClientId</c> simply switches automatic Meet rooms off
/// (instructors paste links), which is a supported production setup.
/// </para>
/// </summary>
public static class LiveProductionRequirements
{
    /// <summary>Human-readable problems (never including secret values); empty when the configuration is acceptable for production.</summary>
    public static IReadOnlyList<string> GetProblems(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var problems = new List<string>();

        var provider = configuration[$"{LiveOptions.SectionName}:Provider"]?.Trim();
        if (string.Equals(provider, nameof(LiveProviderMode.Logging), StringComparison.OrdinalIgnoreCase))
        {
            problems.Add(
                $"{LiveOptions.SectionName}:Provider must not be '{nameof(LiveProviderMode.Logging)}' in production: it fakes Google OAuth and creates fake meeting rooms (development only).");
        }
        else if (!string.IsNullOrEmpty(provider) && !Enum.TryParse<LiveProviderMode>(provider, ignoreCase: true, out _))
        {
            problems.Add($"{LiveOptions.SectionName}:Provider '{provider}' is not one of {string.Join(", ", Enum.GetNames<LiveProviderMode>())}.");
        }

        // The effective public origin (Live:PublicBaseUrl, else Seo:PublicBaseUrl) is put into links learners and instructors follow.
        var publicBaseUrl = configuration[$"{LiveOptions.SectionName}:PublicBaseUrl"];
        if (string.IsNullOrWhiteSpace(publicBaseUrl))
        {
            publicBaseUrl = configuration[LiveOptions.FallbackPublicBaseUrlKey];
        }

        if (string.IsNullOrWhiteSpace(publicBaseUrl) || !publicBaseUrl.Trim().StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            problems.Add($"{LiveOptions.SectionName}:PublicBaseUrl (or {LiveOptions.FallbackPublicBaseUrlKey}) must be the real public https:// origin in production.");
        }

        // P11-13: the dev sample file is what the fake recording provider serves in place of a real Drive download.
        var devSampleFilePath = configuration[$"{LiveOptions.SectionName}:Recording:AutoImport:{nameof(LiveRecordingAutoImportOptions.DevSampleFilePath)}"];
        if (!string.IsNullOrWhiteSpace(devSampleFilePath))
        {
            problems.Add(
                $"{LiveOptions.SectionName}:Recording:AutoImport:{nameof(LiveRecordingAutoImportOptions.DevSampleFilePath)} must be empty in production: it is a development-only stand-in for a recording file.");
        }

        var clientId = configuration[$"{GoogleOAuthOptions.SectionName}:ClientId"];
        if (!string.IsNullOrWhiteSpace(clientId))
        {
            var clientSecret = configuration[$"{GoogleOAuthOptions.SectionName}:ClientSecret"];
            if (string.IsNullOrWhiteSpace(clientSecret) || clientSecret.Contains("CHANGE_ME", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{GoogleOAuthOptions.SectionName}:ClientSecret must be set to the real OAuth client secret in production when ClientId is set (not empty or a placeholder).");
            }

            var redirectUri = configuration[$"{GoogleOAuthOptions.SectionName}:RedirectUri"];
            if (string.IsNullOrWhiteSpace(redirectUri) || !redirectUri.Trim().StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            {
                problems.Add($"{GoogleOAuthOptions.SectionName}:RedirectUri must be an https:// URL (the API's /api/live/instructor/google/callback) in production when ClientId is set.");
            }
        }

        return problems;
    }
}
