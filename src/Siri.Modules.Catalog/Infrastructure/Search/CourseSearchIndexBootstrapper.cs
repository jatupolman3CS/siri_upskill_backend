using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Modules.Catalog.Application;

namespace Siri.Modules.Catalog.Infrastructure.Search;

/// <summary>
/// Runs once when the API host starts: creates the Meilisearch index + settings if they are missing and fills the index when it holds no course yet
/// (<see cref="CourseSearchIndexer.BootstrapAsync"/>), so a fresh deployment — or a newly pointed-at Meilisearch — starts returning results without
/// anyone having to trigger a reindex by hand. It runs in the background and never blocks or fails the host: if Meilisearch is down at that
/// moment the problem is logged, searches use the database fallback, and the hourly <c>course-search-reindex</c> job completes the job later.
/// A populated index is left untouched. Registered in <c>Siri.Api</c> only (the Workers host has the recurring job instead).
/// </summary>
public sealed class CourseSearchIndexBootstrapper(
    IServiceScopeFactory scopeFactory,
    IOptions<MeilisearchOptions> options,
    ILogger<CourseSearchIndexBootstrapper> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        if (!settings.IsActive)
        {
            // The single, never-secret signal of "search runs on the database fallback" — what the operator needs when a result looks different.
            // A URL that is set but unusable (no real key) is a misconfiguration, not a choice, so it is a warning.
            var level = string.IsNullOrWhiteSpace(settings.Url) || !settings.Enabled ? LogLevel.Information : LogLevel.Warning;
            logger.Log(level, "Meilisearch course search is off ({Reason}); the PostgreSQL search is used.", settings.InactiveReason);
            return;
        }

        if (settings.GetBaseAddress() is { Scheme: "http", IsLoopback: false } plainHttp)
        {
            // Not blocked (a private network or tunnel is a legitimate setup) but the bearer key is then sent unencrypted on every call.
            logger.LogWarning(
                "Meilisearch is reached over plain http:// ({Host}); the API key is sent unencrypted. Prefer https:// or a private network address.",
                plainHttp.Host);
        }

        try
        {
            // Let the host finish starting before touching the network or the database.
            await Task.Yield();

            await using var scope = scopeFactory.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<CourseSearchIndexer>().BootstrapAsync(stoppingToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Host is shutting down.
        }
        catch (Exception ex)
        {
            // A BackgroundService that throws takes the whole host down (.NET 6+ default); a search-engine hiccup at boot must not. Logged, not hidden.
            logger.LogWarning(
                ex, "Meilisearch bootstrap failed; search uses the database fallback until the hourly reindex succeeds.");
        }
    }
}
