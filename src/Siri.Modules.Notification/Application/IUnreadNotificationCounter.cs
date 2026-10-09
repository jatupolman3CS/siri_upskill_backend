namespace Siri.Modules.Notification.Application;

/// <summary>
/// The number of unread in-app notifications of a user — what the bell badge shows, and therefore asked on every page load.
/// Served from Redis; recounted from the database (one indexed <c>COUNT</c>) only on a miss. Cache-aside with two invalidation
/// paths: marking a notification read (same module, immediate) and the "notification created" event (Kafka consumer, within a
/// second or two). The short TTL is the safety net for everything else — Redis being briefly down when an invalidation
/// was attempted, or the database-only transport where no event ever arrives — so a stale badge corrects itself within the TTL.
/// Fail-open: Redis unavailable means counting from the database, never an error.
/// </summary>
public interface IUnreadNotificationCounter
{
    Task<int> GetAsync(Guid userId, CancellationToken cancellationToken);

    /// <summary>Forgets the cached count so the next <see cref="GetAsync"/> recounts. Never throws on a Redis failure.</summary>
    Task InvalidateAsync(Guid userId, CancellationToken cancellationToken);
}
