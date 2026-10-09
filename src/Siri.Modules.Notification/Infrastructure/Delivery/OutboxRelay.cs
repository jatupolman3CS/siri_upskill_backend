using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Integrations.Messaging;
using Siri.Modules.Notification.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Notification.Infrastructure.Delivery;

/// <summary>How long a relay waits before its next cycle.</summary>
internal static class RelayPacing
{
    private static readonly TimeSpan FailureBackoffBase = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan FailureBackoffMax = TimeSpan.FromSeconds(30);

    /// <param name="relayed">Rows published by the cycle that just ended.</param>
    /// <param name="batchSize">Rows a full cycle claims.</param>
    /// <param name="consecutiveFailures">Failed cycles in a row, 0 when the last cycle succeeded.</param>
    /// <param name="pollInterval">Idle wait.</param>
    public static TimeSpan NextDelay(int relayed, int batchSize, int consecutiveFailures, TimeSpan pollInterval)
    {
        if (consecutiveFailures > 0)
        {
            // Broker (or database) trouble: back off instead of hammering it, but never beyond 30 s so recovery is noticed promptly.
            return RetryBackoff.For(consecutiveFailures, FailureBackoffBase, FailureBackoffMax);
        }

        // A full batch means there is probably more waiting: go again at once (drains a backlog at full speed); otherwise idle.
        return relayed >= batchSize ? TimeSpan.Zero : pollInterval;
    }
}

/// <summary>
/// The "outbox relay" half of the transactional outbox: a loop that moves rows the application committed to the database onto a
/// Kafka topic. The application never talks to the broker while handling a request (that would be a dual write that can half
/// fail); it only commits rows, and this loop publishes them afterwards. A row is marked published in the same transaction that
/// holds its lock, <i>after</i> the broker acknowledged the record — so a crash anywhere re-publishes (never loses) the record,
/// and consumers are idempotent. Runs in the worker host only.
/// </summary>
internal abstract class OutboxRelayService(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationDeliveryOptions> options,
    ILogger logger) : BackgroundService
{
    protected abstract string Channel { get; }

    /// <summary>One claim → publish → mark → commit cycle. Returns how many rows were published.</summary>
    /// <exception cref="MessagePublishException">Rows were claimed but none could be published (broker unreachable).</exception>
    protected abstract Task<int> RelayOnceAsync(IServiceProvider services, CancellationToken cancellationToken);

    protected NotificationDeliveryOptions Options => options.Value;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // never block host startup

        var failures = 0;

        while (!stoppingToken.IsCancellationRequested)
        {
            var relayed = 0;

            try
            {
                var scope = scopeFactory.CreateAsyncScope();
                await using (scope.ConfigureAwait(false))
                {
                    relayed = await RelayOnceAsync(scope.ServiceProvider, stoppingToken).ConfigureAwait(false);
                }

                failures = 0;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                failures++;
                NotificationTelemetry.RelayFailures.Add(1, new KeyValuePair<string, object?>("channel", Channel));
                logger.LogWarning(
                    ex,
                    "Notification relay '{Channel}' cycle failed ({Failures} in a row); backing off",
                    Channel,
                    failures);
            }

            var delay = RelayPacing.NextDelay(relayed, Options.RelayBatchSize, failures, Options.RelayPollInterval);
            if (delay <= TimeSpan.Zero)
            {
                continue;
            }

            try
            {
                await Task.Delay(delay, stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}

/// <summary>Publishes pending / retry-due / stale-queued email outbox rows to <see cref="NotificationTopics.Email"/>.</summary>
internal sealed class EmailOutboxRelay(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationDeliveryOptions> options,
    NotificationTopics topics,
    ILogger<EmailOutboxRelay> logger) : OutboxRelayService(scopeFactory, options, logger)
{
    protected override string Channel => "email";

    /// <summary>Puts unpublished rows back. Never throws (a failed release only means the rows wait out the stale window) and is not cancellable:
    /// at shutdown the relay still owes these rows their release.</summary>
    private async Task ReleaseQuietlyAsync(IEmailOutboxRelayStore store, EmailClaim claim, IReadOnlyCollection<Guid> notPublished)
    {
        try
        {
            await store.ReleaseAsync(claim, notPublished, CancellationToken.None).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.LogWarning(
                ex,
                "Could not release {Count} email outbox rows after a failed publish; they stay Queued until the stale window passes",
                notPublished.Count);
        }
    }

    protected override async Task<int> RelayOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var store = services.GetRequiredService<IEmailOutboxRelayStore>();
        var producer = services.GetRequiredService<IMessageProducer>();
        var clock = services.GetRequiredService<IClock>();
        var heartbeat = services.GetService<IEmailConsumerHeartbeat>();

        // Rows that have sat in Queued past the stale window are either lost (the relay died between claim and publish, or the record is gone)
        // or merely waiting in Kafka behind a long backlog. If a consumer has finished a record within that same window the topic is being
        // worked through, so they are the second kind and are left alone; only when nothing has been consumed for a whole window are they
        // claimed again. (Redis unavailable counts as "no progress seen": the sweep then runs as it would without the heartbeat.)
        var now = clock.UtcNow;
        var consumersAreWorking = heartbeat is not null
            && await heartbeat.WasActiveWithinAsync(Options.QueuedStaleAfter, cancellationToken).ConfigureAwait(false);
        DateTime? staleBefore = consumersAreWorking ? null : now - Options.QueuedStaleAfter;

        // Step 1 — claim: lock the due rows, mark them Queued, commit. Nothing is held open from here on.
        var claim = await store
            .ClaimAsync(Options.RelayBatchSize, now, staleBefore, cancellationToken)
            .ConfigureAwait(false);

        if (claim.Items.Count == 0)
        {
            return 0;
        }

        var notPublished = new List<Guid>();
        try
        {
            // Step 2 — publish, with no database lock held. A consumer that gets one of these records finds a committed, consistent Queued row.
            var acknowledged = await Task.WhenAll(claim.Items.Select(async item =>
            {
                var record = new EmailDeliveryMessage(item.Id, item.TemplateKey, item.Attempts + 1, claim.ClaimedAtUtc);
                try
                {
                    await producer
                        .ProduceAsync(topics.Email, EmailDeliveryMessage.KeyFor(item.Id), record.ToJson(), headers: null, cancellationToken)
                        .ConfigureAwait(false);
                    return true;
                }
                catch (Exception ex)
                {
                    // Anything that stops a record reaching the broker — a refusal, a timeout, a producer that cannot be built, shutdown — means
                    // "not acknowledged": the row must go back, whatever the reason.
                    logger.LogDebug(ex, "Email record for {MessageId} was not published", item.Id);
                    return false;
                }
            })).ConfigureAwait(false);

            notPublished.AddRange(claim.Items.Where((_, i) => !acknowledged[i]).Select(item => item.Id));
        }
        catch (Exception)
        {
            // Defensive: if publishing itself blew up in a way the per-record handler did not absorb, nothing is known to have been published.
            notPublished.Clear();
            notPublished.AddRange(claim.Items.Select(item => item.Id));
            await ReleaseQuietlyAsync(store, claim, notPublished).ConfigureAwait(false);
            throw;
        }

        // Step 3 — compensate: whatever the broker did not acknowledge goes back exactly as it was, to be tried again next cycle. (If the
        // process dies before this point the rows stay Queued and are claimed again once the stale window passes: delayed, never lost.)
        if (notPublished.Count > 0)
        {
            await ReleaseQuietlyAsync(store, claim, notPublished).ConfigureAwait(false);
        }

        var published = claim.Items.Count - notPublished.Count;
        if (published == 0)
        {
            // Nothing got through: let the loop back off instead of hammering a broker that is down.
            throw new MessagePublishException(topics.Email, $"none of the {claim.Items.Count} claimed records was acknowledged");
        }

        NotificationTelemetry.RelayPublished.Add(published, new KeyValuePair<string, object?>("channel", Channel));
        return published;
    }
}

/// <summary>Publishes a "notification created" event for every in-app notification not yet announced to
/// <see cref="NotificationTopics.InApp"/>.</summary>
internal sealed class InAppNotificationRelay(
    IServiceScopeFactory scopeFactory,
    IOptions<NotificationDeliveryOptions> options,
    NotificationTopics topics,
    ILogger<InAppNotificationRelay> logger) : OutboxRelayService(scopeFactory, options, logger)
{
    protected override string Channel => "inapp";

    /// <summary>Rows retired per idle cycle (see <see cref="IInAppRelayStore.MarkStaleUnpublishedAsync"/>).</summary>
    private const int RetireChunkSize = 1000;

    protected override async Task<int> RelayOnceAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var store = services.GetRequiredService<IInAppRelayStore>();
        var producer = services.GetRequiredService<IMessageProducer>();
        var clock = services.GetRequiredService<IClock>();

        var now = clock.UtcNow;
        var freshAfter = now - Options.InAppEventMaxAge;

        var batch = await store.BeginBatchAsync(Options.RelayBatchSize, freshAfter, cancellationToken).ConfigureAwait(false);

        await using (batch.ConfigureAwait(false))
        {
            if (batch.Items.Count == 0)
            {
                // Idle: retire anything too old to be worth an event (rows written while the database transport was active). The batch's
                // transaction is open on this same context, so the update only becomes durable if the batch is completed (committed).
                // A bounded chunk per idle cycle: a long history is worked off over a few seconds instead of one statement that could outlive the
                // command timeout and be rolled back every time. A full chunk means more may remain, so go again at once.
                var retired = await store
                    .MarkStaleUnpublishedAsync(freshAfter, now, RetireChunkSize, cancellationToken)
                    .ConfigureAwait(false);
                await batch.CompleteAsync(cancellationToken).ConfigureAwait(false);
                return retired >= RetireChunkSize ? Options.RelayBatchSize : 0;
            }

            var acknowledged = await Task.WhenAll(batch.Items.Select(async notification =>
            {
                var record = new InAppNotificationEvent(notification.Id, notification.UserId, notification.Type, notification.CreatedAtUtc);
                try
                {
                    await producer
                        .ProduceAsync(topics.InApp, InAppNotificationEvent.KeyFor(notification.UserId), record.ToJson(), headers: null, cancellationToken)
                        .ConfigureAwait(false);
                    return true;
                }
                catch (MessagePublishException)
                {
                    return false;
                }
            })).ConfigureAwait(false);

            var published = 0;
            for (var i = 0; i < batch.Items.Count; i++)
            {
                if (acknowledged[i])
                {
                    batch.Items[i].MarkPublished(clock);
                    published++;
                }
            }

            if (published == 0)
            {
                throw new MessagePublishException(topics.InApp, $"none of the {batch.Items.Count} claimed records was acknowledged");
            }

            await batch.CompleteAsync(cancellationToken).ConfigureAwait(false);
            NotificationTelemetry.RelayPublished.Add(published, new KeyValuePair<string, object?>("channel", Channel));
            return published;
        }
    }
}
