using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Integrations.Email;
using Siri.Integrations.Google;
using Siri.Modules.Live.Application;
using Siri.Modules.Notification.Contracts;
using Siri.SharedKernel;
using Siri.Workers;

namespace Siri.Api.Diagnostics;

/// <summary>
/// Builds <see cref="LiveAdminStatusResponse"/>: Hangfire's monitoring view (servers + the reported recurring jobs), the e-mail outbox's health, the
/// Live module's configuration and counters, and the warnings derived from all of it (<see cref="LiveStatusWarnings"/>).
/// <para>
/// Read-only throughout, and each source is read independently: if one cannot be read (storage down, a table missing) the others are still reported and a
/// warning says which was unreadable — a diagnostics endpoint must not fall over exactly when something is broken. Only the exception <em>type</em> is logged,
/// never its message (it may carry a connection string or a query).
/// </para>
/// <para>
/// Lives in the host (it composes four modules' contracts and the Hangfire view) and therefore touches no database itself — every number comes through a
/// module's own reader.
/// </para>
/// </summary>
public sealed class LiveAdminStatusService(
    IBackgroundJobStatusReader jobs,
    IEmailOutboxHealthReader outbox,
    ILiveDiagnosticsReader liveCounts,
    IOptions<LiveOptions> liveOptions,
    IGoogleOAuthService google,
    EmailProviderInfo email,
    IClock clock,
    ILogger<LiveAdminStatusService> logger)
{
    public async Task<LiveAdminStatusResponse> GetStatusAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var options = liveOptions.Value;
        var expectedJobIds = RecurringJobIds.LiveDiagnostics;

        // ---- Background jobs (Hangfire's API is synchronous: keep it off the request thread) ----
        var storageReadable = true;
        BackgroundJobStatus job;
        try
        {
            job = await Task.Run(() => jobs.GetStatus(expectedJobIds), cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            storageReadable = false;
            job = new BackgroundJobStatus([], []);
            logger.LogWarning("Live status: Hangfire storage could not be read ({ExceptionType}).", ex.GetType().Name);
        }

        var heartbeatCutoff = now - LiveStatusWarnings.ServerHeartbeatStaleAfter;
        var servers = job.Servers
            .Select(server => new JobServerDto(server.Name, server.StartedAtUtc, server.HeartbeatUtc))
            .ToArray();
        var running = servers.Any(server => server.HeartbeatUtc is { } heartbeat && heartbeat >= heartbeatCutoff);
        var recurringJobs = job.RecurringJobs
            .Select(recurring => new RecurringJobStatusDto(recurring.Id, recurring.Cron, recurring.LastExecutionUtc, recurring.NextExecutionUtc, recurring.LastJobState))
            .ToArray();

        // ---- E-mail outbox ----
        var emailReadable = true;
        EmailOutboxHealth health;
        try
        {
            health = await outbox.GetHealthAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            emailReadable = false;
            health = new EmailOutboxHealth(0, 0, null);
            logger.LogWarning("Live status: the e-mail outbox could not be read ({ExceptionType}).", ex.GetType().Name);
        }

        long? oldestPendingAgeSeconds = health.OldestWaitingQueuedAtUtc is { } queuedAt
            ? Math.Max(0L, (long)(now - queuedAt).TotalSeconds)
            : null;

        // ---- Live ----
        var liveReadable = true;
        LiveOperationalCounts counts;
        try
        {
            counts = await liveCounts.GetCountsAsync(now, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            liveReadable = false;
            counts = new LiveOperationalCounts(0, 0, 0, 0, 0);
            logger.LogWarning("Live status: the Live counters could not be read ({ExceptionType}).", ex.GetType().Name);
        }

        var publicBaseUrl = options.GetNormalizedPublicBaseUrl();

        var warnings = LiveStatusWarnings.Evaluate(new LiveStatusFacts(
            now,
            storageReadable,
            running,
            recurringJobs,
            expectedJobIds,
            email.Name,
            emailReadable,
            health.WaitingCount,
            health.FailedCount,
            oldestPendingAgeSeconds,
            options.Provider.ToString(),
            google.IsConfigured,
            publicBaseUrl,
            liveReadable,
            counts.MeetingsFailed,
            counts.MeetingsStuckPending));

        return new LiveAdminStatusResponse(
            now,
            new JobServerStatusDto(running, servers.Length, servers),
            recurringJobs,
            new EmailStatusDto(email.Name, health.WaitingCount, health.FailedCount, oldestPendingAgeSeconds),
            new LiveSystemStatusDto(
                options.Provider.ToString(),
                google.IsConfigured,
                HostOf(publicBaseUrl),
                counts.MeetingsPending,
                counts.MeetingsAwaitingLink,
                counts.MeetingsFailed,
                counts.InvitesPending),
            warnings);
    }

    /// <summary>Just the host (and a non-default port) of the configured origin — no scheme, path, query or credentials; empty when it is not a valid absolute URL.</summary>
    public static string HostOf(string publicBaseUrl) =>
        Uri.TryCreate(publicBaseUrl, UriKind.Absolute, out var uri)
            ? (uri.IsDefaultPort ? uri.Host : $"{uri.Host}:{uri.Port}") // never uri.UserInfo
            : string.Empty;
}
