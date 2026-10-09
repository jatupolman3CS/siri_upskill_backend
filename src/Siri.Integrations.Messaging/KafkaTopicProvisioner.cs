using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Siri.Integrations.Messaging;

/// <summary>A topic a module needs to exist, with the shape it wants.</summary>
/// <param name="Name">Topic name.</param>
/// <param name="Partitions">Parallelism ceiling: at most this many consumer instances in one group do useful work.</param>
/// <param name="ReplicationFactor">Copies of each partition; cannot exceed the broker count (1 on a single-node cluster).</param>
/// <param name="Retention">How long records are kept; <c>null</c> keeps the broker default.</param>
public sealed record KafkaTopicDefinition(string Name, int Partitions, short ReplicationFactor, TimeSpan? Retention);

/// <summary>
/// Creates the registered <see cref="KafkaTopicDefinition"/>s that do not exist yet, so deploying the application needs no
/// separate "create the topics" step and the partition count is explicit instead of whatever the broker defaults to.
/// <para>
/// A <b>background</b> retry loop, not a blocking startup step: an unreachable broker must never delay the host (the Hangfire
/// server starts in the same process). Creation is idempotent (an existing topic is left untouched — this never alters partitions
/// or retention of a live topic). Lacking permission to create topics (a managed cluster with ACLs) is logged and tolerated:
/// the topics are then expected to be provisioned by the cluster's owner.
/// </para>
/// </summary>
public sealed class KafkaTopicProvisioner(
    IOptions<KafkaOptions> kafkaOptions,
    IEnumerable<KafkaTopicDefinition> topics,
    ILogger<KafkaTopicProvisioner> logger) : BackgroundService
{
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield();

        var definitions = topics.DistinctBy(t => t.Name).ToList();
        if (definitions.Count == 0)
        {
            return;
        }

        for (var attempt = 1; !stoppingToken.IsCancellationRequested; attempt++)
        {
            try
            {
                if (await TryProvisionAsync(definitions, stoppingToken).ConfigureAwait(false))
                {
                    return;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Kafka topic provisioning attempt {Attempt} failed", attempt);
            }

            try
            {
                await Task.Delay(RetryBackoff.For(attempt, TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(1)), stoppingToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    /// <returns><c>true</c> when nothing more needs to be tried (created, already existed, or not permitted).</returns>
    private async Task<bool> TryProvisionAsync(List<KafkaTopicDefinition> definitions, CancellationToken cancellationToken)
    {
        var config = KafkaClientConfig.Apply(new AdminClientConfig(), kafkaOptions.Value, "admin");
        using var admin = new AdminClientBuilder(config).Build();

        var specifications = definitions.Select(d => new TopicSpecification
        {
            Name = d.Name,
            NumPartitions = d.Partitions,
            ReplicationFactor = d.ReplicationFactor,
            Configs = d.Retention is { } retention
                ? new Dictionary<string, string> { ["retention.ms"] = ((long)retention.TotalMilliseconds).ToString(System.Globalization.CultureInfo.InvariantCulture) }
                : null,
        }).ToList();

        try
        {
            await admin.CreateTopicsAsync(specifications, new CreateTopicsOptions { RequestTimeout = RequestTimeout })
                .WaitAsync(cancellationToken)
                .ConfigureAwait(false);
            logger.LogInformation("Kafka topics ensured: {Topics}", string.Join(", ", definitions.Select(d => d.Name)));
            return true;
        }
        catch (CreateTopicsException ex)
        {
            var failures = ex.Results.Where(r => r.Error.Code != ErrorCode.NoError && r.Error.Code != ErrorCode.TopicAlreadyExists).ToList();
            if (failures.Count == 0)
            {
                logger.LogInformation("Kafka topics already exist: {Topics}", string.Join(", ", definitions.Select(d => d.Name)));
                return true;
            }

            if (failures.All(f => f.Error.Code == ErrorCode.TopicAuthorizationFailed))
            {
                logger.LogWarning(
                    "Not permitted to create Kafka topics {Topics}; assuming they are provisioned by the cluster owner",
                    string.Join(", ", failures.Select(f => f.Topic)));
                return true;
            }

            throw;
        }
    }
}
