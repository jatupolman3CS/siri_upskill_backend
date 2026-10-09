using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Siri.Api.Diagnostics;

/// <summary>
/// Production only: shortly after the API has started, runs the same checks as <c>GET /api/live/admin/status</c> <b>once</b> and writes a single log
/// line — at Warning when something is wrong — so an operator reading the pod logs sees "no_job_server", "email_unconfigured", ... without anyone having to
/// call the endpoint (the failure it is aimed at is precisely that nobody knows the background jobs are not running).
/// <para>
/// The check waits a while first (<see cref="StartupDelay"/>): a Hangfire server registers itself within seconds, and a dedicated Workers deployment may
/// start a little later than the API — reporting at the very first instant would cry wolf. The line carries only the warning codes, <c>serverInApi</c>
/// and the count — no value, no secret, no address. Any failure of the check itself is logged (type only) and swallowed: diagnostics must never stop the host.
/// </para>
/// </summary>
public sealed class LiveStatusStartupReporter(
    IServiceScopeFactory scopeFactory,
    IHostEnvironment environment,
    Microsoft.Extensions.Options.IOptions<Siri.Workers.HangfireHostingOptions> hosting,
    ILogger<LiveStatusStartupReporter> logger) : BackgroundService
{
    /// <summary>How long after startup the check runs (tests set it to zero).</summary>
    public TimeSpan StartupDelay { get; init; } = TimeSpan.FromSeconds(90);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!environment.IsProduction())
        {
            return;
        }

        try
        {
            await Task.Delay(StartupDelay, stoppingToken).ConfigureAwait(false);

            await using var scope = scopeFactory.CreateAsyncScope();
            var status = await scope.ServiceProvider.GetRequiredService<LiveAdminStatusService>()
                .GetStatusAsync(stoppingToken)
                .ConfigureAwait(false);

            if (status.Warnings.Count == 0)
            {
                logger.LogInformation(
                    "Live system check: no warnings (job server running={JobServerRunning}, serverInApi={ServerInApi}).",
                    status.JobServer.Running,
                    hosting.Value.ServerInApi);
                return;
            }

            logger.LogWarning(
                "Live system check found {WarningCount} warning(s): {Warnings} (job server running={JobServerRunning}, serverInApi={ServerInApi}). See GET /api/live/admin/status.",
                status.Warnings.Count,
                string.Join(",", status.Warnings),
                status.JobServer.Running,
                hosting.Value.ServerInApi);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // shutting down before the check ran
        }
        catch (Exception ex)
        {
            logger.LogWarning("Live system check could not run ({ExceptionType}).", ex.GetType().Name);
        }
    }
}
