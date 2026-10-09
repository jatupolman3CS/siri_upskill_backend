using System.Text.RegularExpressions;
using Confluent.Kafka;

namespace Siri.Integrations.Messaging;

/// <summary>
/// Connection settings for the Kafka cluster, bound from configuration section <see cref="SectionName"/> ("Kafka").
/// Shared platform infrastructure like <c>Redis</c> and <c>ConnectionStrings:Default</c> — it is not owned by any one
/// module; topic names, partition counts and consumer groups belong to the module that uses them (for Notification:
/// <c>Notification:Delivery</c>). Registered only by hosts that actually run a Kafka pipeline
/// (<see cref="MessagingServiceCollectionExtensions.AddKafkaMessaging"/>), so a host without Kafka needs no value here.
/// <para>
/// Credentials (<see cref="SaslUsername"/>/<see cref="SaslPassword"/>) are secrets: user-secrets in dev, environment in
/// prod — never committed (security.md).
/// </para>
/// </summary>
public sealed partial class KafkaOptions
{
    public const string SectionName = "Kafka";

    /// <summary>Comma-separated <c>host:port</c> list of brokers (the "bootstrap" set — the client discovers the rest).</summary>
    public string BootstrapServers { get; set; } = string.Empty;

    /// <summary>Prefix for the client ids this process reports to the broker (shows up in broker logs/quotas).</summary>
    public string ClientId { get; set; } = "siri-upskill";

    /// <summary>
    /// Prefix of every topic this application uses on the cluster: a module's topic is <c>{TopicPrefix}.{module-specific-name}</c>
    /// (and its consumer groups share the prefix). It is what lets several environments — or several applications — share one
    /// cluster without ever reading each other's records: give each its own (<c>siriupskill-dev</c>, <c>siriupskill</c>, ...).
    /// Letters, digits, <c>.</c>, <c>_</c> and <c>-</c> only.
    /// </summary>
    public string TopicPrefix { get; set; } = "siriupskill";

    /// <summary>Copies kept of each partition of the topics the application creates. Cannot exceed the broker count — 1 on a single-node cluster.</summary>
    public short ReplicationFactor { get; set; } = 1;

    /// <summary>Transport security. <see cref="Confluent.Kafka.SecurityProtocol.Plaintext"/> and <see cref="Confluent.Kafka.SecurityProtocol.SaslPlaintext"/>
    /// send everything — records and, for the latter, the SASL password — unencrypted, so they are only acceptable on a private network
    /// (the broker's port is never published); see <see cref="AllowPlaintext"/>.</summary>
    public SecurityProtocol SecurityProtocol { get; set; } = SecurityProtocol.Plaintext;

    /// <summary>Required (with username and password) when <see cref="SecurityProtocol"/> is a <c>Sasl*</c> value.</summary>
    public SaslMechanism? SaslMechanism { get; set; }

    public string? SaslUsername { get; set; }

    public string? SaslPassword { get; set; }

    /// <summary>Path to a CA bundle to verify the broker's certificate with (<c>Ssl</c>/<c>SaslSsl</c>); empty = the OS trust store.</summary>
    public string? SslCaLocation { get; set; }

    /// <summary>Explicit acknowledgement that an unencrypted connection is intentional (broker on a private docker network, no
    /// published port). Production refuses to start with <see cref="Confluent.Kafka.SecurityProtocol.Plaintext"/> or
    /// <see cref="Confluent.Kafka.SecurityProtocol.SaslPlaintext"/> unless this is <c>true</c> — see <see cref="KafkaProductionRequirements"/>.</summary>
    public bool AllowPlaintext { get; set; }

    /// <summary>How long a produce call may take before it is reported as failed (covers broker outage + retries).</summary>
    public int ProduceTimeoutSeconds { get; set; } = 15;

    [GeneratedRegex("^[A-Za-z0-9][A-Za-z0-9._-]{0,59}$", RegexOptions.CultureInvariant)]
    internal static partial Regex TopicPrefixPattern();

    internal bool UsesSasl => SecurityProtocol is SecurityProtocol.SaslPlaintext or SecurityProtocol.SaslSsl;

    /// <summary>True when nothing on the connection is encrypted (no TLS) — the case <see cref="AllowPlaintext"/> must acknowledge.</summary>
    internal bool IsUnencrypted => SecurityProtocol is SecurityProtocol.Plaintext or SecurityProtocol.SaslPlaintext;
}
