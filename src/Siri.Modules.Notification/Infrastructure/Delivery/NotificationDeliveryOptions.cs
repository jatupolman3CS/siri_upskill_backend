using Microsoft.Extensions.Options;

namespace Siri.Modules.Notification.Infrastructure.Delivery;

/// <summary>How queued notifications travel from the database outbox to the recipient.</summary>
public enum NotificationTransport
{
    /// <summary>The original path: the Hangfire <c>email-outbox-send</c> job polls <c>NOTIFY.EMAIL_OUTBOX</c> every minute and
    /// sends over SMTP itself. No broker needed. This is the default, so a deployment without Kafka behaves exactly as before.</summary>
    Database,

    /// <summary>Outbox relay → Kafka → consumers. Needs <c>Kafka:*</c> configuration and a reachable broker. The Hangfire sender
    /// stands down (it would otherwise double-send).</summary>
    Kafka,
}

/// <summary>
/// Bound from configuration section <see cref="SectionName"/>. Read by both hosts (the API only to ignore it; the worker host
/// is where the pipeline runs). Everything here has a working default, so only <see cref="Transport"/> needs flipping to adopt Kafka.
/// The topic and consumer-group <i>names</i> are not configured one by one: they are derived from the cluster-wide
/// <c>Kafka:TopicPrefix</c> (see <see cref="NotificationTopics"/>), which is what keeps environments on a shared cluster apart.
/// </summary>
public sealed class NotificationDeliveryOptions
{
    public const string SectionName = "Notification:Delivery";

    public NotificationTransport Transport { get; set; } = NotificationTransport.Database;

    /// <summary>Partitions of each work topic created by the provisioner. Also the ceiling on useful consumer instances.</summary>
    public int Partitions { get; set; } = 3;

    public int TopicRetentionDays { get; set; } = 7;

    /// <summary>Email consumers run in this process (same group). Raise it (up to <see cref="Partitions"/>) when SMTP latency, not the broker, is the bottleneck.</summary>
    public int EmailConsumerInstances { get; set; } = 1;

    /// <summary>Rows claimed and published per relay cycle.</summary>
    public int RelayBatchSize { get; set; } = 100;

    /// <summary>Pause between relay cycles when the last one found nothing to do (a full batch is followed by another cycle at once).</summary>
    public int RelayPollIntervalMs { get; set; } = 1000;

    /// <summary>A <c>Queued</c> row with no recorded outcome for this long is published again (record lost, or its consumer died) — but only while
    /// no consumer has finished a record within the same window, so a long backlog that is merely being worked through is left alone. At least 10:
    /// it must outlast the 5-minute delivery claim plus the slowest SMTP send, or healthy deliveries get duplicated. Size it above the time your
    /// largest expected burst needs to drain (recipients ÷ send rate) if you want to be sure rows waiting behind it are never re-claimed.</summary>
    public int QueuedStaleAfterMinutes { get; set; } = 15;

    /// <summary>In-app events are only worth publishing while fresh (they invalidate a cache); anything older that was never
    /// published — typically rows written while the database transport was active — is marked published without an event.</summary>
    public int InAppEventMaxAgeHours { get; set; } = 24;

    /// <summary>Cluster-wide ceiling on emails handed to SMTP per minute (a Redis counter shared by every consumer instance);
    /// 0 = unlimited. Absorbs bursts (an announcement to thousands of learners) so the mail provider is not tripped.</summary>
    public int MaxEmailsPerMinute { get; set; }

    /// <summary>How long a cached unread-notification count may be served without a database re-count.</summary>
    public int UnreadCountCacheSeconds { get; set; } = 30;

    public TimeSpan QueuedStaleAfter => TimeSpan.FromMinutes(QueuedStaleAfterMinutes);

    public TimeSpan InAppEventMaxAge => TimeSpan.FromHours(InAppEventMaxAgeHours);

    public TimeSpan RelayPollInterval => TimeSpan.FromMilliseconds(RelayPollIntervalMs);
}

/// <summary>
/// The names of the topics and consumer groups the notification pipeline uses, all derived from the cluster-wide topic prefix so
/// that two environments (or applications) on one cluster can never read each other's records.
/// </summary>
/// <param name="Email">Claim-check records for emails to send (the body stays in the database).</param>
/// <param name="EmailDeadLetter">Email records that could not be processed at all (malformed, or the consumer kept failing). Operator-owned: never auto-consumed.</param>
/// <param name="InApp">"A notification was created for this user" events.</param>
/// <param name="InAppDeadLetter">In-app events that could not be processed.</param>
/// <param name="EmailGroup">Consumer group of the email-delivery consumers.</param>
/// <param name="InAppGroup">Consumer group of the in-app event consumer.</param>
public sealed record NotificationTopics(
    string Email,
    string EmailDeadLetter,
    string InApp,
    string InAppDeadLetter,
    string EmailGroup,
    string InAppGroup)
{
    /// <summary><c>{prefix}.notification.email.v1</c> etc. The <c>v1</c> is the record schema version: an incompatible payload change gets a
    /// <c>v2</c> topic, so old and new consumers never read a shape they do not understand.</summary>
    public static NotificationTopics For(string topicPrefix)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(topicPrefix);

        return new NotificationTopics(
            Email: $"{topicPrefix}.notification.email.v1",
            EmailDeadLetter: $"{topicPrefix}.notification.email.dlq.v1",
            InApp: $"{topicPrefix}.notification.inapp.v1",
            InAppDeadLetter: $"{topicPrefix}.notification.inapp.dlq.v1",
            EmailGroup: $"{topicPrefix}.notification.email",
            InAppGroup: $"{topicPrefix}.notification.inapp");
    }
}

/// <summary>Rejects an unusable delivery configuration at start (<c>ValidateOnStart</c>) rather than at the first relay cycle.</summary>
internal sealed class NotificationDeliveryOptionsValidator : IValidateOptions<NotificationDeliveryOptions>
{
    /// <summary>Longer than the delivery claim (5 min) with room for a slow send.</summary>
    internal const int MinimumStaleAfterMinutes = 10;

    public ValidateOptionsResult Validate(string? name, NotificationDeliveryOptions options)
    {
        var problems = new List<string>();

        // Used whatever the transport: the unread-count cache everywhere, the stale-queued window by the database-polling job (which adopts
        // rows a Kafka relay left Queued after an operator switched back).
        if (options.UnreadCountCacheSeconds is < 1 or > 3600)
        {
            problems.Add("Notification:Delivery:UnreadCountCacheSeconds must be between 1 and 3600.");
        }

        if (options.QueuedStaleAfterMinutes < MinimumStaleAfterMinutes)
        {
            problems.Add(
                $"Notification:Delivery:QueuedStaleAfterMinutes must be at least {MinimumStaleAfterMinutes}: it has to outlast the 5-minute delivery claim " +
                "plus the slowest SMTP send, or an email that is still being delivered gets claimed a second time.");
        }

        if (options.Transport != NotificationTransport.Kafka)
        {
            return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
        }

        if (options.Partitions < 1)
        {
            problems.Add("Notification:Delivery:Partitions must be at least 1.");
        }

        if (options.TopicRetentionDays < 1)
        {
            problems.Add("Notification:Delivery:TopicRetentionDays must be at least 1.");
        }

        if (options.EmailConsumerInstances < 1 || options.EmailConsumerInstances > 32)
        {
            problems.Add("Notification:Delivery:EmailConsumerInstances must be between 1 and 32.");
        }

        if (options.RelayBatchSize is < 1 or > 1000)
        {
            problems.Add("Notification:Delivery:RelayBatchSize must be between 1 and 1000.");
        }

        if (options.RelayPollIntervalMs is < 50 or > 60_000)
        {
            problems.Add("Notification:Delivery:RelayPollIntervalMs must be between 50 and 60000.");
        }

        if (options.InAppEventMaxAgeHours < 1)
        {
            problems.Add("Notification:Delivery:InAppEventMaxAgeHours must be at least 1.");
        }

        if (options.MaxEmailsPerMinute < 0)
        {
            problems.Add("Notification:Delivery:MaxEmailsPerMinute must not be negative (0 = unlimited).");
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
