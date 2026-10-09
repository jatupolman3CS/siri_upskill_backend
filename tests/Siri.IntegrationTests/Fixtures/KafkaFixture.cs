using System.Net;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Testcontainers.Kafka;

namespace Siri.IntegrationTests.Fixtures;

/// <summary>
/// A real Kafka broker for the notification-pipeline tests. Class-level fixture (<c>IClassFixture&lt;KafkaFixture&gt;</c>), so only the
/// test classes that need a broker pay for one — the rest of the suite (and its <see cref="ContainersFixture"/>) is untouched.
/// <para>
/// Three ways to get a broker, in this order:
/// </para>
/// <list type="number">
/// <item><b>Live mode</b> — <see cref="LiveBootstrapEnvVar"/> names a broker on a <i>shared development cluster you own</i> (SASL login via
/// the sibling <c>SIRI_IT_KAFKA_LIVE_*</c> variables). The only mode that may leave the machine, so it is deliberately a separate,
/// loudly named opt-in. Tests only ever create topics and consumer groups whose names start with <see cref="TestTopicPrefix"/>, and this
/// fixture deletes everything with that prefix when it is disposed — it never touches a topic that belongs to anything else. Credentials come
/// from the environment of the test process only; they are never written to a file.</item>
/// <item><b>Local external mode</b> — <see cref="ExternalEnvVar"/> is the bootstrap address of an unauthenticated broker already running on
/// this machine (loopback only, refused otherwise), e.g. <c>127.0.0.1:19092</c>.</item>
/// <item><b>Testcontainers</b> (default; needs Docker) — a throwaway <c>apache/kafka</c> KRaft node, the same image family production runs.</item>
/// </list>
/// Every test uses topic names carrying a random suffix, so runs never see each other's records in any mode.
/// </summary>
public sealed class KafkaFixture : IAsyncLifetime
{
    public const string ExternalEnvVar = "SIRI_IT_KAFKA";
    public const string LiveBootstrapEnvVar = "SIRI_IT_KAFKA_LIVE_BOOTSTRAP";
    public const string LiveProtocolEnvVar = "SIRI_IT_KAFKA_LIVE_PROTOCOL";
    public const string LiveMechanismEnvVar = "SIRI_IT_KAFKA_LIVE_MECHANISM";
    public const string LiveUsernameEnvVar = "SIRI_IT_KAFKA_LIVE_USERNAME";
    public const string LivePasswordEnvVar = "SIRI_IT_KAFKA_LIVE_PASSWORD";

    /// <summary>Pinned to the tag the deployment docs use (docs/DEPLOYMENT.md "Kafka"); change both together.</summary>
    private const string Image = "apache/kafka:4.2.2";

    /// <summary>Every topic prefix a test uses starts with this, so anything an interrupted run leaves on a broker is recognisable (and
    /// removable) without touching a topic that belongs to anything else.</summary>
    public const string TestTopicPrefix = "siriupskill-it-";

    private KafkaContainer? _container;
    private SecurityProtocol _securityProtocol = SecurityProtocol.Plaintext;
    private SaslMechanism? _saslMechanism;
    private string? _saslUsername;
    private string? _saslPassword;

    public string BootstrapServers { get; private set; } = string.Empty;

    /// <summary>True for a shared development cluster reached through <see cref="LiveBootstrapEnvVar"/>.</summary>
    public bool IsLive { get; private set; }

    /// <summary>Extra <c>Kafka:*</c> settings the broker needs (a SASL login, TLS) merged into every test's configuration. Empty for an
    /// unauthenticated broker.</summary>
    public IReadOnlyDictionary<string, string?> ExtraConfiguration { get; private set; } = new Dictionary<string, string?>();

    public async Task InitializeAsync()
    {
        var live = Environment.GetEnvironmentVariable(LiveBootstrapEnvVar);
        if (!string.IsNullOrWhiteSpace(live))
        {
            InitializeLive(live);
            return;
        }

        var external = Environment.GetEnvironmentVariable(ExternalEnvVar);
        if (!string.IsNullOrWhiteSpace(external))
        {
            foreach (var endpoint in external.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var host = endpoint.Contains(':', StringComparison.Ordinal) ? endpoint[..endpoint.LastIndexOf(':')] : endpoint;
                if (!(string.Equals(host, "localhost", StringComparison.OrdinalIgnoreCase) ||
                      (IPAddress.TryParse(host, out var address) && IPAddress.IsLoopback(address))))
                {
                    throw new InvalidOperationException(
                        $"{ExternalEnvVar} refuses '{host}': it may only name a broker on a loopback address. " +
                        $"For a shared development cluster use {LiveBootstrapEnvVar}.");
                }
            }

            BootstrapServers = external;
            return;
        }

        _container = new KafkaBuilder(Image).Build();
        await _container.StartAsync();
        BootstrapServers = _container.GetBootstrapAddress();
    }

    private void InitializeLive(string bootstrap)
    {
        IsLive = true;
        BootstrapServers = bootstrap;

        var protocol = Environment.GetEnvironmentVariable(LiveProtocolEnvVar);
        _securityProtocol = string.IsNullOrWhiteSpace(protocol)
            ? SecurityProtocol.Plaintext
            : Enum.Parse<SecurityProtocol>(protocol, ignoreCase: true);

        var settings = new Dictionary<string, string?> { ["Kafka:SecurityProtocol"] = _securityProtocol.ToString() };

        var mechanism = Environment.GetEnvironmentVariable(LiveMechanismEnvVar);
        if (!string.IsNullOrWhiteSpace(mechanism))
        {
            _saslMechanism = Enum.Parse<SaslMechanism>(mechanism, ignoreCase: true);
            settings["Kafka:SaslMechanism"] = _saslMechanism.ToString();
        }

        _saslUsername = Environment.GetEnvironmentVariable(LiveUsernameEnvVar);
        _saslPassword = Environment.GetEnvironmentVariable(LivePasswordEnvVar);
        if (!string.IsNullOrWhiteSpace(_saslUsername))
        {
            settings["Kafka:SaslUsername"] = _saslUsername;
            settings["Kafka:SaslPassword"] = _saslPassword;
        }

        ExtraConfiguration = settings;
    }

    public async Task DisposeAsync()
    {
        if (IsLive)
        {
            await DeleteEverythingWithTheTestPrefixAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }

    /// <summary>Applies the connection and login of this fixture's broker to a client configuration.</summary>
    private T Secure<T>(T config)
        where T : ClientConfig
    {
        config.BootstrapServers = BootstrapServers;
        config.SecurityProtocol = _securityProtocol;
        if (_saslMechanism is not null)
        {
            config.SaslMechanism = _saslMechanism;
            config.SaslUsername = _saslUsername;
            config.SaslPassword = _saslPassword;
        }

        return config;
    }

    /// <summary>Removes the topics and consumer groups the tests created on a shared cluster — and only those: the name must start with
    /// <see cref="TestTopicPrefix"/>. Best effort; anything it misses is still recognisable by the prefix.</summary>
    private async Task DeleteEverythingWithTheTestPrefixAsync()
    {
        try
        {
            using var admin = new AdminClientBuilder(Secure(new AdminClientConfig())).Build();
            var timeout = TimeSpan.FromSeconds(20);

            var topics = admin.GetMetadata(timeout).Topics
                .Select(t => t.Topic)
                .Where(name => name.StartsWith(TestTopicPrefix, StringComparison.Ordinal))
                .ToList();
            if (topics.Count > 0)
            {
                await admin.DeleteTopicsAsync(topics, new DeleteTopicsOptions { RequestTimeout = timeout });
            }

            var groups = admin.ListGroups(timeout)
                .Select(g => g.Group)
                .Where(name => name.StartsWith(TestTopicPrefix, StringComparison.Ordinal))
                .ToList();
            if (groups.Count > 0)
            {
                try
                {
                    await admin.DeleteGroupsAsync(groups, new DeleteGroupsOptions { RequestTimeout = timeout });
                }
                catch (DeleteGroupsException ex)
                {
                    // A group that is gone already (its topics were just deleted) or still shutting down is not worth failing a run over.
                    var codes = string.Join(",", ex.Results.Where(r => r.Error.IsError).Select(r => r.Error.Code).Distinct());
                    Console.Error.WriteLine($"[KafkaFixture] some '{TestTopicPrefix}*' consumer groups were not deleted ({codes}); they are empty and harmless");
                }
            }
        }
        catch (Exception ex) when (ex is KafkaException or DeleteTopicsException or TimeoutException)
        {
            Console.Error.WriteLine($"[KafkaFixture] could not clean up '{TestTopicPrefix}*' topics on the shared cluster: {ex.GetType().Name}");
        }
    }

    /// <summary>Reads every record currently on <paramref name="topic"/> from the beginning (a throwaway consumer group), for assertions.</summary>
    public IReadOnlyList<ConsumeResult<string, string>> ReadAll(string topic, TimeSpan timeout)
    {
        var config = Secure(new ConsumerConfig
        {
            GroupId = $"{TestTopicPrefix}reader-{Guid.NewGuid():N}",
            AutoOffsetReset = AutoOffsetReset.Earliest,
            EnableAutoCommit = false,
            EnablePartitionEof = true,
        });

        using var consumer = new ConsumerBuilder<string, string>(config).Build();
        consumer.Subscribe(topic);

        var results = new List<ConsumeResult<string, string>>();
        var deadline = DateTime.UtcNow + timeout;
        var eofPartitions = new HashSet<int>();
        var assigned = 0;

        while (DateTime.UtcNow < deadline)
        {
            ConsumeResult<string, string>? result;
            try
            {
                result = consumer.Consume(TimeSpan.FromMilliseconds(500));
            }
            catch (ConsumeException)
            {
                continue; // the topic may not exist yet; keep waiting until the deadline
            }

            if (result is null)
            {
                continue;
            }

            if (result.IsPartitionEOF)
            {
                eofPartitions.Add(result.Partition.Value);
                assigned = Math.Max(assigned, consumer.Assignment.Count);
                if (assigned > 0 && eofPartitions.Count >= assigned)
                {
                    break;
                }

                continue;
            }

            results.Add(result);
        }

        consumer.Close(); // leave the throwaway group at once, so the fixture can delete it

        return results;
    }
}
