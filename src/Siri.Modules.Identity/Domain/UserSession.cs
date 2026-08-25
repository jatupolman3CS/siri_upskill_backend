using Siri.SharedKernel;

namespace Siri.Modules.Identity.Domain;

/// <summary>
/// One device/browser's login session for a <see cref="User"/>. Exists to support the
/// concurrent-login-limit enforcement (SE-03, default <c>MaxConcurrentSessions = 2</c>) — that
/// enforcement logic itself is a later task; this is only the entity/schema it runs on.
/// </summary>
public sealed class UserSession
{
    /// <summary>EF Core materialization only.</summary>
    private UserSession()
    {
    }

    public Guid Id { get; private set; }

    public Guid UserId { get; private set; }

    public string DeviceId { get; private set; } = string.Empty;

    public string? DeviceName { get; private set; }

    public string? UserAgent { get; private set; }

    public string? IpAddress { get; private set; }

    public DateTime CreatedAtUtc { get; private set; }

    public DateTime LastSeenAtUtc { get; private set; }

    public DateTime? RevokedAtUtc { get; private set; }

    public string? RevokeReason { get; private set; }

    public bool IsActive => RevokedAtUtc is null;

    public static UserSession Start(
        Guid userId,
        string deviceId,
        string? deviceName,
        string? userAgent,
        string? ipAddress,
        IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(deviceId);
        ArgumentNullException.ThrowIfNull(clock);

        var now = clock.UtcNow;

        return new UserSession
        {
            Id = UuidV7.NewId(),
            UserId = userId,
            DeviceId = deviceId,
            DeviceName = deviceName,
            UserAgent = userAgent,
            IpAddress = ipAddress,
            CreatedAtUtc = now,
            LastSeenAtUtc = now,
        };
    }

    public void Revoke(string reason, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        ArgumentNullException.ThrowIfNull(clock);

        if (!IsActive)
        {
            return; // idempotent — already revoked
        }

        RevokedAtUtc = clock.UtcNow;
        RevokeReason = reason;
    }

    /// <summary>Bumps <see cref="LastSeenAtUtc"/> to now — called on each successful refresh (P0-16)
    /// so the column actually reflects "last activity" rather than only ever holding its value from
    /// <see cref="Start"/>. A no-op on an already-revoked session (its activity timestamp stops
    /// mattering once it can no longer be used), same idempotent-no-op style as <see cref="Revoke"/>.</summary>
    public void Touch(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);

        if (!IsActive)
        {
            return;
        }

        LastSeenAtUtc = clock.UtcNow;
    }
}
