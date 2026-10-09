using System.Diagnostics.Metrics;

namespace Siri.Modules.Notification.Infrastructure.Delivery;

/// <summary>
/// Counters for the notification pipeline. Hosts opt in with <c>.AddMeter(NotificationTelemetry.MeterName)</c>; with no listener
/// they cost nothing. What an operator should alert on: <c>exhausted</c> &gt; 0 (a mail gave up for good),
/// <c>relay.failures</c> climbing (the broker is unreachable), and <c>email.delivered</c> flat while the outbox grows.
/// </summary>
public static class NotificationTelemetry
{
    public const string MeterName = "Siri.Notification";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>Rows handed to the broker, by channel (<c>email</c> | <c>inapp</c>).</summary>
    internal static readonly Counter<long> RelayPublished = Meter.CreateCounter<long>("notification.relay.published");

    /// <summary>Relay cycles that failed to publish (broker down, timeout), by channel.</summary>
    internal static readonly Counter<long> RelayFailures = Meter.CreateCounter<long>("notification.relay.failures");

    internal static readonly Counter<long> EmailDelivered = Meter.CreateCounter<long>("notification.email.delivered");

    /// <summary>A delivery attempt failed; a retry is scheduled.</summary>
    internal static readonly Counter<long> EmailFailed = Meter.CreateCounter<long>("notification.email.failed");

    /// <summary>The last allowed attempt failed — the message is a dead letter that needs a person.</summary>
    internal static readonly Counter<long> EmailExhausted = Meter.CreateCounter<long>("notification.email.exhausted");

    /// <summary>A record needed no work, by <c>reason</c> (<c>already_sent</c> | <c>already_delivered</c> | <c>in_flight</c> | <c>missing</c> | <c>exhausted</c>).</summary>
    internal static readonly Counter<long> EmailSkipped = Meter.CreateCounter<long>("notification.email.skipped");

    /// <summary>Times a consumer waited for the cluster-wide send budget.</summary>
    internal static readonly Counter<long> EmailThrottled = Meter.CreateCounter<long>("notification.email.throttled");

    internal static readonly Counter<long> InAppEvents = Meter.CreateCounter<long>("notification.inapp.events");
}
