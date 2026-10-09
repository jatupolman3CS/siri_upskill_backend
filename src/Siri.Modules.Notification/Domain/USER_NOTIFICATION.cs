using Siri.SharedKernel;

namespace Siri.Modules.Notification.Domain;

/// <summary>
/// An in-app notification delivered to a specific user.
/// </summary>
public sealed class USER_NOTIFICATION
{
    private USER_NOTIFICATION()
    {
    }

    public Guid Id { get; private set; }
    public Guid UserId { get; private set; }
    public string Type { get; private set; } = string.Empty;
    public string Title { get; private set; } = string.Empty;
    public string Body { get; private set; } = string.Empty;
    public string? LinkUrl { get; private set; }
    public DateTime? ReadAtUtc { get; private set; }
    public DateTime CreatedAtUtc { get; private set; }

    /// <summary>When the "notification created" event was handed to the message broker; <c>null</c> until then. The row is the
    /// event's outbox record: writers stage it in their own transaction, and the relay publishes whatever is still <c>null</c>.
    /// Under the database-only transport nothing publishes, so it simply stays <c>null</c>.</summary>
    public DateTime? PublishedAtUtc { get; private set; }

    public static USER_NOTIFICATION Create(
        Guid userId,
        string type,
        string title,
        string body,
        string? linkUrl,
        IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentException.ThrowIfNullOrWhiteSpace(title);
        ArgumentException.ThrowIfNullOrWhiteSpace(body);

        return new USER_NOTIFICATION
        {
            Id = UuidV7.NewId(),
            UserId = userId,
            Type = type,
            Title = title,
            Body = body,
            LinkUrl = linkUrl,
            ReadAtUtc = null,
            CreatedAtUtc = clock.UtcNow,
        };
    }

    /// <summary>Records that the creation event reached the broker. Idempotent — the first timestamp wins.</summary>
    public void MarkPublished(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        PublishedAtUtc ??= clock.UtcNow;
    }

    public void MarkRead(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ReadAtUtc = clock.UtcNow;
    }
}
