using Confluent.Kafka;
using Microsoft.Extensions.Configuration;

namespace Siri.Integrations.Messaging;

/// <summary>
/// What the Kafka connection must look like before a host may start in Production — the shared helper both the worker host
/// and any future Kafka-using host call, so their guards cannot drift apart (same shape as
/// <c>EmailProductionRequirements</c>). Only meaningful for a host that runs a Kafka pipeline; the caller decides that.
/// </summary>
public static class KafkaProductionRequirements
{
    /// <summary>Human-readable problems (never including secret values); empty when the connection settings are acceptable.</summary>
    public static IReadOnlyList<string> GetProblems(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        var section = configuration.GetSection(KafkaOptions.SectionName);
        var options = section.Get<KafkaOptions>() ?? new KafkaOptions();
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(options.BootstrapServers))
        {
            problems.Add("Kafka:BootstrapServers is not set (the notification pipeline is configured to use Kafka).");
        }

        if (options.IsUnencrypted && !options.AllowPlaintext)
        {
            problems.Add(
                "Kafka:SecurityProtocol is Plaintext/SaslPlaintext, so records (and any SASL password) cross the network unencrypted. Use Ssl/SaslSsl, or — only when the broker is reachable solely on a private network — " +
                "set Kafka:AllowPlaintext=true to acknowledge it.");
        }

        return problems;
    }
}
