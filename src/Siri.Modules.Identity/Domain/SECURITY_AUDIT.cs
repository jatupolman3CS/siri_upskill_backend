using Siri.SharedKernel;

namespace Siri.Modules.Identity.Domain;

/// <summary>
/// An immutable security event log entry (failed login, password reset requested, session revoked,
/// ...). <see cref="UserId"/> is nullable because some events have no resolved user yet — e.g. a
/// failed login against an email that doesn't exist. Append-only by design: there is deliberately
/// no method to change an entry after creation.
/// </summary>
public sealed class SECURITY_AUDIT
{
    /// <summary>EF Core materialization only.</summary>
    private SECURITY_AUDIT()
    {
    }

    public Guid Id { get; private set; }

    public Guid? UserId { get; private set; }

    public string EventType { get; private set; } = string.Empty;

    /// <summary>Structured (JSON) detail about the event. Genuinely unbounded, so it's the one
    /// intentional exception to the "always HasMaxLength" string-column rule.</summary>
    public string? Detail { get; private set; }

    public string? IpAddress { get; private set; }

    public DateTime OccurredAtUtc { get; private set; }

    public static SECURITY_AUDIT Record(string eventType, Guid? userId, string? detail, string? ipAddress, IClock clock)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(eventType);
        ArgumentNullException.ThrowIfNull(clock);

        return new SECURITY_AUDIT
        {
            Id = UuidV7.NewId(),
            UserId = userId,
            EventType = eventType,
            Detail = detail,
            IpAddress = ipAddress,
            OccurredAtUtc = clock.UtcNow,
        };
    }
}
