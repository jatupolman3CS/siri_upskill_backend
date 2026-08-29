using Microsoft.Extensions.Logging;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Media.Application;
using Siri.SharedKernel;

namespace Siri.Modules.Media.Infrastructure;

/// <summary>
/// Hangfire recurring job that detects abnormal video playback token issuance patterns (P2-06).
/// Flags accounts requesting excessive playback tokens (>30 episodes/hour) or concurrent access from
/// multiple distinct IP addresses within a rolling 1-hour window.
/// </summary>
public sealed class PlaybackAnomalyDetectionJob(
    IPlaybackSessionRepository sessionRepository,
    ISecurityAuditContract securityAuditContract,
    IClock clock,
    ILogger<PlaybackAnomalyDetectionJob> logger)
{
    public const int DefaultMaxSessionsPerHour = 30;
    public const int DefaultMaxDistinctIpsPerHour = 2;

    public async Task<int> RunAsync(
        CancellationToken cancellationToken = default,
        int maxSessionsPerHour = DefaultMaxSessionsPerHour,
        int maxDistinctIpsPerHour = DefaultMaxDistinctIpsPerHour)
    {
        var sinceUtc = clock.UtcNow.AddHours(-1);
        var activities = await sessionRepository.GetUserActivitySinceAsync(sinceUtc, cancellationToken).ConfigureAwait(false);

        var anomaliesCount = 0;

        foreach (var activity in activities)
        {
            if (activity.SessionCount > maxSessionsPerHour)
            {
                anomaliesCount++;
                logger.LogWarning(
                    "Playback anomaly: User {UserId} requested {Count} playback tokens in the last hour (threshold: {Threshold})",
                    activity.UserId,
                    activity.SessionCount,
                    maxSessionsPerHour);

                await securityAuditContract.RecordAuditAsync(
                    eventType: "playback.anomaly.excessive_requests",
                    userId: activity.UserId,
                    detail: $"{activity.SessionCount} playback sessions requested in 1 hour (threshold: {maxSessionsPerHour})",
                    ipAddress: activity.LastIpAddress,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }

            if (activity.DistinctIpCount > maxDistinctIpsPerHour)
            {
                anomaliesCount++;
                var ipListStr = string.Join(", ", activity.IpAddresses);
                logger.LogWarning(
                    "Playback anomaly: User {UserId} requested playback from {IpCount} distinct IPs in the last hour: [{Ips}] (threshold: {Threshold})",
                    activity.UserId,
                    activity.DistinctIpCount,
                    ipListStr,
                    maxDistinctIpsPerHour);

                await securityAuditContract.RecordAuditAsync(
                    eventType: "playback.anomaly.multi_ip_detected",
                    userId: activity.UserId,
                    detail: $"{activity.DistinctIpCount} distinct IPs ({ipListStr}) in 1 hour (threshold: {maxDistinctIpsPerHour})",
                    ipAddress: activity.LastIpAddress,
                    cancellationToken: cancellationToken).ConfigureAwait(false);
            }
        }

        return anomaliesCount;
    }
}
