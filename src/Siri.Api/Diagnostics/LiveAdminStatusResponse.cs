namespace Siri.Api.Diagnostics;

/// <summary>
/// <c>GET /api/live/admin/status</c> — an administrator's one-glance answer to "is the live-course system actually working?": is a background
/// job server running, are the recurring jobs scheduled and ticking, can e-mail leave, how is the Live provider configured, and what is queued or stuck.
/// <para>
/// Aggregates only. It never carries a secret, an e-mail address, a room URL, a token or a connection string — <see cref="LiveSystemStatusDto.PublicBaseUrl"/>
/// is the host alone, and every count is an integer. <see cref="Warnings"/> are stable machine-readable codes (<see cref="LiveStatusWarnings"/>), not prose.
/// </para>
/// </summary>
public sealed record LiveAdminStatusResponse(
    DateTime ServerTimeUtc,
    JobServerStatusDto JobServer,
    IReadOnlyList<RecurringJobStatusDto> RecurringJobs,
    EmailStatusDto Email,
    LiveSystemStatusDto Live,
    IReadOnlyList<string> Warnings);

/// <param name="Running">At least one registered Hangfire server has a fresh heartbeat. <c>false</c> means nothing is processing background jobs.</param>
/// <param name="ServerCount">Servers currently registered in storage (a server that died stays listed until Hangfire expires it; check <c>heartbeatUtc</c>).</param>
public sealed record JobServerStatusDto(bool Running, int ServerCount, IReadOnlyList<JobServerDto> Servers);

public sealed record JobServerDto(string Name, DateTime StartedAtUtc, DateTime? HeartbeatUtc);

public sealed record RecurringJobStatusDto(string Id, string Cron, DateTime? LastExecutionUtc, DateTime? NextExecutionUtc, string? LastJobState);

/// <param name="Provider"><c>Smtp</c> (delivers), <c>Log</c> (deliberately delivers nothing) or <c>Unconfigured</c> (every send fails and is retried later).</param>
/// <param name="PendingCount">Messages waiting to be sent or retried (capped).</param>
/// <param name="FailedCount">Messages abandoned after every retry (capped).</param>
/// <param name="OldestPendingAgeSeconds">How long the oldest waiting message has been queued, or <c>null</c> when none is waiting.</param>
public sealed record EmailStatusDto(string Provider, int PendingCount, int FailedCount, long? OldestPendingAgeSeconds);

/// <param name="Provider"><c>GoogleMeet</c>, <c>ManualOnly</c> or <c>Logging</c> (development fake).</param>
/// <param name="GoogleConfigured">Whether the Google OAuth client is configured (instructors can connect Google).</param>
/// <param name="PublicBaseUrl">The host (and non-default port) of the public site the Live links point at — no scheme, path or query.</param>
public sealed record LiveSystemStatusDto(
    string Provider,
    bool GoogleConfigured,
    string PublicBaseUrl,
    int MeetingsPending,
    int MeetingsAwaitingLink,
    int MeetingsFailed,
    int InvitesPending);
