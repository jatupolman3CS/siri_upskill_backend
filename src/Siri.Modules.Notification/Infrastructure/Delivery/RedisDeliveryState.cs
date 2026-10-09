using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.SharedKernel;
using StackExchange.Redis;

namespace Siri.Modules.Notification.Infrastructure.Delivery;

/// <summary>Result of trying to take exclusive responsibility for delivering one outbox message.</summary>
public enum DeliveryClaim
{
    /// <summary>Nobody else is delivering it and it has not been delivered: go ahead.</summary>
    Claimed,

    /// <summary>An earlier run already sent it (Redis remembers even if the database row never recorded it).</summary>
    AlreadyDelivered,

    /// <summary>Another consumer holds the claim right now.</summary>
    InFlightElsewhere,
}

/// <summary>What <see cref="IEmailDeliveryGuard.TryClaimAsync"/> found. <see cref="Token"/> proves ownership of a granted claim: only the holder of
/// the token can release it, so a holder whose claim already expired can never delete the claim of whoever took over. It is <c>null</c> when nothing was
/// claimed (another party holds it, it was delivered) and also when Redis was unavailable and delivery goes ahead unguarded — releasing a
/// <c>null</c> token is a no-op.</summary>
public readonly record struct EmailClaimTicket(DeliveryClaim Outcome, string? Token);

/// <summary>
/// Redis-backed idempotency for email delivery. Kafka redelivers (rebalance, crash before the offset commit) and the relay may
/// publish a row twice, so the same message id can reach a consumer more than once — sometimes at the same instant on two
/// instances. The database row alone cannot stop a duplicate <i>SMTP send</i>: a mail can leave the building and the process die
/// before the row records it. This guard narrows that window to "crash between SMTP accepting the mail and one Redis write".
/// <para>
/// <b>Fail-open by design</b>, like every Redis use in this codebase: Redis being down must not stop mail. Without it the pipeline
/// falls back to the database row's status — at-least-once, with a slightly wider duplicate window. Never a lost email.
/// </para>
/// </summary>
public interface IEmailDeliveryGuard
{
    Task<EmailClaimTicket> TryClaimAsync(Guid messageId, CancellationToken cancellationToken);

    /// <summary>Remembers that the message was sent. Call as soon as SMTP accepted it, <i>before</i> recording it in the database.</summary>
    Task MarkDeliveredAsync(Guid messageId, CancellationToken cancellationToken);

    /// <summary>Gives up the claim after a failed attempt so the retry (or another instance) can take it. Only acts if <paramref name="token"/> still
    /// owns the claim; never touches a "delivered" marker.</summary>
    Task ReleaseAsync(Guid messageId, string? token, CancellationToken cancellationToken);
}

internal sealed class RedisEmailDeliveryGuard(IConnectionMultiplexer redis, ILogger<RedisEmailDeliveryGuard> logger) : IEmailDeliveryGuard
{
    /// <summary>A claim outlives the slowest plausible send (SMTP timeout × a few) but not so long that a crashed holder blocks the message for long.</summary>
    internal static readonly TimeSpan ClaimLifetime = TimeSpan.FromMinutes(5);

    /// <summary>How long "already sent" is remembered: longer than the broker retains the record and the relay's stale window, so any redelivery is still recognised.</summary>
    internal static readonly TimeSpan DeliveredLifetime = TimeSpan.FromDays(7);

    private const string InFlightPrefix = "inflight:";
    private const string Delivered = "delivered";

    /// <summary>The claim is attempted again if its key expires in the instant between "someone holds it" and "let me look who" — otherwise that
    /// record would be dropped with nobody delivering it.</summary>
    private const int MaxClaimAttempts = 3;

    internal static RedisKey KeyFor(Guid messageId) => $"notify:email:{messageId:N}";

    public async Task<EmailClaimTicket> TryClaimAsync(Guid messageId, CancellationToken cancellationToken)
    {
        try
        {
            var db = redis.GetDatabase();
            var key = KeyFor(messageId);

            for (var attempt = 0; attempt < MaxClaimAttempts; attempt++)
            {
                var token = Guid.NewGuid().ToString("N");
                if (await db.StringSetAsync(key, InFlightPrefix + token, ClaimLifetime, When.NotExists).ConfigureAwait(false))
                {
                    return new EmailClaimTicket(DeliveryClaim.Claimed, token);
                }

                var current = await db.StringGetAsync(key).ConfigureAwait(false);
                if (current == Delivered)
                {
                    return new EmailClaimTicket(DeliveryClaim.AlreadyDelivered, null);
                }

                if (current.HasValue)
                {
                    return new EmailClaimTicket(DeliveryClaim.InFlightElsewhere, null);
                }

                // The key vanished between the two calls (the holder's claim just expired or was released): take it.
            }

            return new EmailClaimTicket(DeliveryClaim.InFlightElsewhere, null);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis unavailable; delivering {MessageId} without the duplicate guard", messageId);
            return new EmailClaimTicket(DeliveryClaim.Claimed, null);
        }
    }

    public async Task MarkDeliveredAsync(Guid messageId, CancellationToken cancellationToken)
    {
        try
        {
            await redis.GetDatabase().StringSetAsync(KeyFor(messageId), Delivered, DeliveredLifetime).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis unavailable; could not remember that {MessageId} was delivered", messageId);
        }
    }

    public async Task ReleaseAsync(Guid messageId, string? token, CancellationToken cancellationToken)
    {
        if (token is null)
        {
            return; // nothing was claimed (Redis was down, or the claim was never ours)
        }

        try
        {
            var db = redis.GetDatabase();
            var key = KeyFor(messageId);

            // Atomic compare-and-delete (StackExchange.Redis' own lock-release primitive): the key is removed only while it still holds OUR token.
            // If our claim expired and someone else took over, their claim (and any "delivered" marker) is left alone.
            await db.LockReleaseAsync(key, InFlightPrefix + token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            // The claim simply expires on its own after ClaimLifetime.
            logger.LogWarning(ex, "Redis unavailable; could not release the claim on {MessageId}", messageId);
        }
    }
}

/// <summary>
/// "Are the email consumers making progress?" — a heartbeat the consumers refresh as they work through records and the relay reads before it
/// re-claims rows that have sat in <c>Queued</c> past the stale window. Without it, a long backlog (an announcement to thousands of learners behind a
/// slow SMTP server or the per-minute budget) is indistinguishable from lost records, and the relay would republish the whole waiting tail every
/// stale window. With it, the stale sweep only runs when nothing has been consumed for that long — i.e. when the records really are not coming.
/// Fail-open: if Redis cannot answer, the answer is "no recent progress" and the sweep runs as it would without this class.
/// </summary>
public interface IEmailConsumerHeartbeat
{
    /// <summary>Notes that a consumer just finished handling a record.</summary>
    Task BeatAsync(CancellationToken cancellationToken);

    /// <summary>True when a consumer finished a record within <paramref name="window"/>.</summary>
    Task<bool> WasActiveWithinAsync(TimeSpan window, CancellationToken cancellationToken);
}

internal sealed class RedisEmailConsumerHeartbeat(
    IConnectionMultiplexer redis,
    NotificationTopics topics,
    IClock clock,
    ILogger<RedisEmailConsumerHeartbeat> logger) : IEmailConsumerHeartbeat
{
    /// <summary>At most one Redis write per this interval per process, however fast records flow.</summary>
    private static readonly TimeSpan BeatEvery = TimeSpan.FromSeconds(1);

    private long _lastBeatTicks;

    // Per consumer group, so environments (or tests) sharing a Redis never see each other's progress.
    private RedisKey Key => $"notify:email:heartbeat:{topics.EmailGroup}";

    public async Task BeatAsync(CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        var last = Interlocked.Read(ref _lastBeatTicks);
        if (last != 0 && now.Ticks - last < BeatEvery.Ticks)
        {
            return;
        }

        Interlocked.Exchange(ref _lastBeatTicks, now.Ticks);

        try
        {
            // Expires on its own: a stopped fleet stops looking active.
            await redis.GetDatabase().StringSetAsync(Key, now.Ticks, TimeSpan.FromHours(2)).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis unavailable; consumer heartbeat not recorded");
        }
    }

    public async Task<bool> WasActiveWithinAsync(TimeSpan window, CancellationToken cancellationToken)
    {
        try
        {
            var stored = await redis.GetDatabase().StringGetAsync(Key).ConfigureAwait(false);
            return stored.TryParse(out long ticks) && clock.UtcNow.Ticks - ticks <= window.Ticks;
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis unavailable; assuming the consumers are not making progress");
            return false;
        }
    }
}

/// <summary>
/// Cluster-wide ceiling on emails handed to SMTP per minute. Kafka makes bursts easy to absorb (an announcement to ten thousand
/// learners is ten thousand records within seconds) and cheap to parallelise; the mail provider is the part that cannot take it.
/// A consumer asks for a slot before each send; over budget, it waits for the next minute. A counter in Redis is shared by every
/// consumer instance, so the limit holds however many are running. Fail-open: no Redis, no throttle.
/// </summary>
public interface IEmailSendThrottle
{
    Task WaitForSlotAsync(CancellationToken cancellationToken);
}

internal sealed class RedisEmailSendThrottle(
    IConnectionMultiplexer redis,
    IOptions<NotificationDeliveryOptions> options,
    IClock clock,
    ILogger<RedisEmailSendThrottle> logger) : IEmailSendThrottle
{
    /// <summary>Windows a single send waits through at most before giving up on throttling and proceeding — the limit smooths load, it is not a security control.</summary>
    private const int MaxWindowsToWait = 3;

    /// <summary>The fixed one-minute window containing <paramref name="nowUtc"/>: its counter key and how long until the next window opens.</summary>
    internal static (string Key, TimeSpan UntilNextWindow) WindowFor(DateTime nowUtc)
    {
        var windowStart = new DateTime(nowUtc.Year, nowUtc.Month, nowUtc.Day, nowUtc.Hour, nowUtc.Minute, 0, DateTimeKind.Utc);
        return ($"notify:email:rate:{windowStart:yyyyMMddHHmm}", windowStart.AddMinutes(1) - nowUtc);
    }

    public async Task WaitForSlotAsync(CancellationToken cancellationToken)
    {
        var limit = options.Value.MaxEmailsPerMinute;
        if (limit <= 0)
        {
            return;
        }

        try
        {
            var db = redis.GetDatabase();

            for (var window = 0; window < MaxWindowsToWait; window++)
            {
                var (key, untilNext) = WindowFor(clock.UtcNow);

                var count = await db.StringIncrementAsync(key).ConfigureAwait(false);
                if (count == 1)
                {
                    // First send of this window: let the counter clean itself up (two windows' worth, in case of clock skew between hosts).
                    await db.KeyExpireAsync(key, TimeSpan.FromMinutes(2)).ConfigureAwait(false);
                }

                if (count <= limit)
                {
                    return;
                }

                NotificationTelemetry.EmailThrottled.Add(1);
                logger.LogInformation(
                    "Email send budget of {Limit}/min is used up; waiting {DelaySeconds:F0}s for the next window",
                    limit,
                    untilNext.TotalSeconds);

                // A little past the boundary so the new window's counter exists when we look.
                await Task.Delay(untilNext + TimeSpan.FromMilliseconds(250), cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex) when (ex is RedisException or TimeoutException)
        {
            logger.LogWarning(ex, "Redis unavailable; sending without the per-minute throttle");
        }
    }
}
