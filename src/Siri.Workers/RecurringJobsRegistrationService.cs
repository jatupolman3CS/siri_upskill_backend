using Hangfire;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Siri.Workers;

/// <summary>
/// Schedules the platform's recurring jobs (<see cref="RecurringJobsRegistration.MapRecurringJobs"/>) from inside a host that also runs the
/// Hangfire server — that is, <c>Siri.Api</c> when <c>Hangfire:ServerInApi</c> is on. (The dedicated <c>Siri.Workers</c> host calls the same
/// method itself, synchronously, right after building.)
/// <para>
/// Registration is create-or-update with fixed job ids, so doing it from an API replica <em>and</em> from a Workers deployment (or from several
/// replicas) simply rewrites identical definitions. It runs off the startup path and retries: a database that is a few seconds late must delay the
/// schedule, not stop the web server from starting. It never throws; after the last attempt it logs an error and gives up — the next start tries again.
/// </para>
/// </summary>
public sealed class RecurringJobsRegistrationService(IServiceProvider services, ILogger<RecurringJobsRegistrationService> logger) : BackgroundService
{
    /// <summary>Attempts before giving up (with <see cref="RetryDelay"/> between them).</summary>
    public const int MaxAttempts = 20;

    /// <summary>Wait between attempts (tests set it to zero).</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        // Let host startup finish first: resolving Hangfire's manager opens the storage (a database connection).
        await Task.Yield();

        for (var attempt = 1; attempt <= MaxAttempts; attempt++)
        {
            try
            {
                // Resolved here, not injected: constructing it touches the database, and a failure must be retried, not crash the host.
                services.GetRequiredService<IRecurringJobManager>().MapRecurringJobs();
                logger.LogInformation("Recurring background jobs registered by this host (attempt {Attempt}).", attempt);
                return;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                if (attempt == MaxAttempts)
                {
                    logger.LogError(ex, "Could not register the recurring background jobs after {Attempts} attempts; they will not be scheduled by this host until it restarts.", MaxAttempts);
                    return;
                }

                logger.LogWarning("Could not register the recurring background jobs yet ({ExceptionType}); retrying (attempt {Attempt} of {Max}).", ex.GetType().Name, attempt, MaxAttempts);

                try
                {
                    await Task.Delay(RetryDelay, stoppingToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return;
                }
            }
        }
    }
}
