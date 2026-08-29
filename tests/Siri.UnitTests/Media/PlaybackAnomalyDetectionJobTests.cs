using Microsoft.Extensions.Logging.Abstractions;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Media.Application;
using Siri.Modules.Media.Domain;
using Siri.Modules.Media.Infrastructure;
using Siri.SharedKernel;
using Xunit;

namespace Siri.UnitTests.Media;

public sealed class PlaybackAnomalyDetectionJobTests
{
    private sealed class FakeClock(DateTime now) : IClock
    {
        public DateTime UtcNow => now;
    }

    private sealed class FakePlaybackSessionRepository : IPlaybackSessionRepository
    {
        public readonly List<PLAYBACK_SESSION> Sessions = [];
        public readonly List<PlaybackUserActivity> PredefinedActivities = [];

        public Task<PLAYBACK_SESSION?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
            Task.FromResult(Sessions.FirstOrDefault(s => s.PLAYBACK_SESSION_ID == id));

        public Task<(IReadOnlyList<PLAYBACK_SESSION> Items, int TotalCount)> GetPagedByUserIdAsync(
            Guid userId, int page, int pageSize, CancellationToken cancellationToken)
        {
            var items = Sessions.Where(s => s.USER_ID == userId).ToList();
            return Task.FromResult(((IReadOnlyList<PLAYBACK_SESSION>)items, items.Count));
        }

        public Task<IReadOnlyList<PlaybackUserActivity>> GetUserActivitySinceAsync(
            DateTime sinceUtc, CancellationToken cancellationToken)
        {
            if (PredefinedActivities.Count > 0)
            {
                return Task.FromResult<IReadOnlyList<PlaybackUserActivity>>(PredefinedActivities);
            }

            var activities = Sessions
                .Where(s => s.ISSUED_AT_UTC >= sinceUtc)
                .GroupBy(s => s.USER_ID)
                .Select(g =>
                {
                    var ips = g.Select(s => s.IP_ADDRESS).Where(ip => !string.IsNullOrEmpty(ip)).Select(ip => ip!).Distinct().ToList();
                    return new PlaybackUserActivity(
                        UserId: g.Key,
                        SessionCount: g.Count(),
                        DistinctIpCount: ips.Count,
                        LastIpAddress: ips.LastOrDefault(),
                        IpAddresses: ips);
                })
                .ToList();

            return Task.FromResult<IReadOnlyList<PlaybackUserActivity>>(activities);
        }

        public void Add(PLAYBACK_SESSION playbackSession) => Sessions.Add(playbackSession);

        public Task SaveChangesAsync(CancellationToken cancellationToken) => Task.CompletedTask;
    }

    private sealed class FakeSecurityAuditContract : ISecurityAuditContract
    {
        public readonly List<(string EventType, Guid? UserId, string? Detail, string? IpAddress)> Audits = [];

        public Task RecordAuditAsync(
            string eventType,
            Guid? userId,
            string? detail,
            string? ipAddress,
            CancellationToken cancellationToken = default)
        {
            Audits.Add((eventType, userId, detail, ipAddress));
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task RunAsync_WhenNoActivity_ReportsZeroAnomalies()
    {
        var sessionRepo = new FakePlaybackSessionRepository();
        var auditContract = new FakeSecurityAuditContract();
        var clock = new FakeClock(DateTime.UtcNow);
        var job = new PlaybackAnomalyDetectionJob(
            sessionRepo, auditContract, clock, NullLogger<PlaybackAnomalyDetectionJob>.Instance);

        var count = await job.RunAsync(CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Empty(auditContract.Audits);
    }

    [Fact]
    public async Task RunAsync_WhenNormalUsage_ReportsZeroAnomalies()
    {
        var sessionRepo = new FakePlaybackSessionRepository();
        var auditContract = new FakeSecurityAuditContract();
        var clock = new FakeClock(DateTime.UtcNow);

        var normalUser = Guid.NewGuid();
        sessionRepo.PredefinedActivities.Add(new PlaybackUserActivity(
            UserId: normalUser,
            SessionCount: 5,
            DistinctIpCount: 1,
            LastIpAddress: "203.0.113.1",
            IpAddresses: ["203.0.113.1"]));

        var job = new PlaybackAnomalyDetectionJob(
            sessionRepo, auditContract, clock, NullLogger<PlaybackAnomalyDetectionJob>.Instance);

        var count = await job.RunAsync(CancellationToken.None);

        Assert.Equal(0, count);
        Assert.Empty(auditContract.Audits);
    }

    [Fact]
    public async Task RunAsync_WhenExcessiveTokensRequested_FlagsAnomalyAndRecordsAudit()
    {
        var sessionRepo = new FakePlaybackSessionRepository();
        var auditContract = new FakeSecurityAuditContract();
        var clock = new FakeClock(DateTime.UtcNow);

        var suspiciousUser = Guid.NewGuid();
        sessionRepo.PredefinedActivities.Add(new PlaybackUserActivity(
            UserId: suspiciousUser,
            SessionCount: 35, // > 30 threshold
            DistinctIpCount: 1,
            LastIpAddress: "203.0.113.10",
            IpAddresses: ["203.0.113.10"]));

        var job = new PlaybackAnomalyDetectionJob(
            sessionRepo, auditContract, clock, NullLogger<PlaybackAnomalyDetectionJob>.Instance);

        var count = await job.RunAsync(CancellationToken.None, maxSessionsPerHour: 30, maxDistinctIpsPerHour: 2);

        Assert.Equal(1, count);
        Assert.Single(auditContract.Audits);
        Assert.Equal("playback.anomaly.excessive_requests", auditContract.Audits[0].EventType);
        Assert.Equal(suspiciousUser, auditContract.Audits[0].UserId);
        Assert.Contains("35 playback sessions", auditContract.Audits[0].Detail);
    }

    [Fact]
    public async Task RunAsync_WhenMultipleDistinctIpsDetected_FlagsAnomalyAndRecordsAudit()
    {
        var sessionRepo = new FakePlaybackSessionRepository();
        var auditContract = new FakeSecurityAuditContract();
        var clock = new FakeClock(DateTime.UtcNow);

        var multiIpUser = Guid.NewGuid();
        sessionRepo.PredefinedActivities.Add(new PlaybackUserActivity(
            UserId: multiIpUser,
            SessionCount: 10,
            DistinctIpCount: 4, // > 2 threshold
            LastIpAddress: "198.51.100.4",
            IpAddresses: ["198.51.100.1", "198.51.100.2", "198.51.100.3", "198.51.100.4"]));

        var job = new PlaybackAnomalyDetectionJob(
            sessionRepo, auditContract, clock, NullLogger<PlaybackAnomalyDetectionJob>.Instance);

        var count = await job.RunAsync(CancellationToken.None, maxSessionsPerHour: 30, maxDistinctIpsPerHour: 2);

        Assert.Equal(1, count);
        Assert.Single(auditContract.Audits);
        Assert.Equal("playback.anomaly.multi_ip_detected", auditContract.Audits[0].EventType);
        Assert.Equal(multiIpUser, auditContract.Audits[0].UserId);
        Assert.Contains("4 distinct IPs", auditContract.Audits[0].Detail);
    }
}
