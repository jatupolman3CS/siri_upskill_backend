using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;

namespace Siri.Workers;

/// <summary>
/// Where the Hangfire <b>processing server</b> runs (configuration section <see cref="SectionName"/>, <c>Hangfire</c>). Options pattern +
/// <c>ValidateOnStart()</c> like every other option in the system.
/// <para>
/// Background work (the e-mail outbox, the Live room sync/invites/reminders, enrollment recount, payment reconciliation, ...) only happens
/// when a Hangfire server is running. There are two places it can run, and the system works with either or both:
/// </para>
/// <list type="bullet">
/// <item><b>Inside <c>Siri.Api</c></b> (<see cref="ServerInApi"/> = <c>true</c>, the default) — a single-container deployment just works.</item>
/// <item><b>In a dedicated <c>Siri.Workers</c> deployment</b> — set <c>Hangfire__ServerInApi=false</c> on the API so the web process only
/// enqueues work and serves the dashboard.</item>
/// </list>
/// Running both at once is safe — see <c>docs/DEPLOYMENT.md</c> ("Background jobs").
/// </summary>
public sealed class HangfireHostingOptions
{
    public const string SectionName = "Hangfire";

    /// <summary>The full configuration key (env: <c>Hangfire__ServerInApi</c>).</summary>
    public const string ServerInApiKey = SectionName + ":" + nameof(ServerInApi);

    /// <summary>The environment the integration-test host runs under (<c>SiriApiFactory</c>). Background jobs never start there unless a test asks for them explicitly.</summary>
    public const string IntegrationTestEnvironmentName = "IntegrationTest";

    /// <summary>Whether <c>Siri.Api</c> itself runs the Hangfire processing server and schedules the recurring jobs. Default <c>true</c>.</summary>
    public bool ServerInApi { get; set; } = true;

    /// <summary>
    /// The effective value for a host: the explicit setting when there is one, otherwise <c>true</c> — except under the <c>IntegrationTest</c>
    /// environment, where a missing setting means <c>false</c> so a test host never spins up background jobs by accident (a test that wants them sets the
    /// key explicitly). A value that is not a boolean is a configuration error and throws at startup.
    /// </summary>
    public static bool ResolveServerInApi(IConfiguration configuration, IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var raw = configuration[ServerInApiKey];
        if (string.IsNullOrWhiteSpace(raw))
        {
            return !environment.IsEnvironment(IntegrationTestEnvironmentName);
        }

        if (!bool.TryParse(raw.Trim(), out var value))
        {
            throw new InvalidOperationException($"'{ServerInApiKey}' must be 'true' or 'false' (got '{raw}').");
        }

        return value;
    }
}
