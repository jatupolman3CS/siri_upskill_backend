using Siri.SharedKernel;

namespace Siri.Modules.Notification.Domain;

/// <summary>
/// An in-app notification delivered to a specific user.
/// </summary>
public sealed class UserNotification
{
    private UserNotification()
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

    public static UserNotification Create(
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

        return new UserNotification
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

    public void MarkRead(IClock clock)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ReadAtUtc = clock.UtcNow;
    }
}
