using System.Text.Json;
using Siri.Integrations.Messaging;

namespace Siri.Modules.Notification.Infrastructure.Delivery;

/// <summary>
/// The value of a record on the email topic: a <b>claim check</b>. It names the outbox row and nothing else — the subject, the
/// HTML body (which can carry password-reset and email-confirmation links) and the recipient address never enter the broker. That
/// keeps secrets out of topic retention and out of any future consumer's reach, keeps records tiny whatever the body size (an
/// invite's calendar file can be 200 KB), and keeps the database the single source of truth for the message's state.
/// </summary>
/// <param name="MessageId">The <c>EMAIL_OUTBOX_MESSAGE.Id</c> to deliver.</param>
/// <param name="TemplateKey">Which template produced it — for tracing a record by eye; no behaviour depends on it.</param>
/// <param name="Attempt">1-based number of the delivery attempt this publish asks for (informational).</param>
/// <param name="QueuedAtUtc">When the relay published it.</param>
public sealed record EmailDeliveryMessage(Guid MessageId, string? TemplateKey, int Attempt, DateTime QueuedAtUtc)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <exception cref="PoisonMessageException">Not JSON of this shape, or no message id — it can never be delivered.</exception>
    public static EmailDeliveryMessage Parse(string json)
    {
        EmailDeliveryMessage? message;
        try
        {
            message = JsonSerializer.Deserialize<EmailDeliveryMessage>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new PoisonMessageException("Email delivery record is not valid JSON.", ex);
        }

        if (message is null || message.MessageId == Guid.Empty)
        {
            throw new PoisonMessageException("Email delivery record has no message id.");
        }

        return message;
    }

    /// <summary>
    /// The record key: the outbox message id. Every publish of the same message (the first, a retry, a stale re-claim) lands on the same
    /// partition and is therefore consumed in the order it was published, while different messages spread evenly across partitions.
    /// Deliberately <b>not</b> derived from the recipient's address: the key is readable by anyone who can read the topic or the broker
    /// logs, and even a hash of an email address is guessable from a list of candidate addresses — an email address is personal data (PDPA)
    /// that the claim check exists to keep off the broker. (Ordering across a recipient's different emails was never guaranteed anyway:
    /// retries and republishes reorder them.)
    /// </summary>
    public static string KeyFor(Guid messageId) => messageId.ToString("N");
}

/// <summary>The value of a record on the in-app topic: "this user has a new notification". Carries ids and a type only — no title or
/// body — because its one job is to tell consumers (today: the unread-count cache) that something changed.</summary>
public sealed record InAppNotificationEvent(Guid NotificationId, Guid UserId, string Type, DateTime CreatedAtUtc)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public string ToJson() => JsonSerializer.Serialize(this, JsonOptions);

    /// <exception cref="PoisonMessageException">Not JSON of this shape, or an id is missing.</exception>
    public static InAppNotificationEvent Parse(string json)
    {
        InAppNotificationEvent? notificationEvent;
        try
        {
            notificationEvent = JsonSerializer.Deserialize<InAppNotificationEvent>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new PoisonMessageException("In-app notification event is not valid JSON.", ex);
        }

        if (notificationEvent is null || notificationEvent.NotificationId == Guid.Empty || notificationEvent.UserId == Guid.Empty)
        {
            throw new PoisonMessageException("In-app notification event is missing an id.");
        }

        return notificationEvent;
    }

    /// <summary>Key by user: one user's events stay in order, and the cache invalidation is naturally per-user.</summary>
    public static string KeyFor(Guid userId) => userId.ToString("N");
}
