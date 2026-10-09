using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Integrations.Messaging;
using Siri.Modules.Notification.Application;
using StackExchange.Redis;

namespace Siri.Modules.Notification.Infrastructure.Delivery;

/// <summary>Redis cache-aside implementation of <see cref="IUnreadNotificationCounter"/>; the contract's doc comment describes the behaviour.</summary>
internal sealed class UnreadNotificationCounter(
    IConnectionMultiplexer redis,
    IUserNotificationRepository repository,
    IOptions<NotificationDeliveryOptions> options,
    ILogger<UnreadNotificationCounter> logger) : IUnreadNotificationCounter
{
    internal static RedisKey KeyFor(Guid userId) => $"notify:unread:{userId:N}";

    public async Task<int> GetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var key = KeyFor(userId);

        try
        {
            var cached = await redis.GetDatabase().StringGetAsync(key).ConfigureAwait(false);
            if (cached.HasValue && cached.TryParse(out int count) && count >= 0)
            {
                return count;
            }
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis unavailable; counting unread notifications from the database");
        }

        var fresh = await repository.GetUnreadCountAsync(userId, cancellationToken).ConfigureAwait(false);

        try
        {
            await redis.GetDatabase()
                .StringSetAsync(key, fresh, TimeSpan.FromSeconds(options.Value.UnreadCountCacheSeconds))
                .ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis unavailable; unread count not cached");
        }

        return fresh;
    }

    public async Task InvalidateAsync(Guid userId, CancellationToken cancellationToken)
    {
        try
        {
            await redis.GetDatabase().KeyDeleteAsync(KeyFor(userId)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            // The cached value expires on its own within UnreadCountCacheSeconds.
            logger.LogWarning(ex, "Redis unavailable; unread count cache not invalidated");
        }
    }
}

/// <summary>Reacts to "a notification was created for this user": drops that user's cached unread count. Idempotent — deleting a
/// missing key is a no-op — so redelivery and duplicate events are harmless.</summary>
internal sealed class InAppNotificationHandler(IUnreadNotificationCounter counter) : IMessageHandler
{
    public async Task HandleAsync(ConsumedMessage message, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(message);

        var notificationEvent = InAppNotificationEvent.Parse(message.Value);
        await counter.InvalidateAsync(notificationEvent.UserId, cancellationToken).ConfigureAwait(false);
        NotificationTelemetry.InAppEvents.Add(1);
    }
}
