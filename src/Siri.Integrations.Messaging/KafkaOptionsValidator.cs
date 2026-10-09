using Microsoft.Extensions.Options;

namespace Siri.Integrations.Messaging;

/// <summary>Fails the host at start (<c>ValidateOnStart</c>) on an unusable Kafka configuration instead of letting the
/// first produce/consume discover it minutes later.</summary>
internal sealed class KafkaOptionsValidator : IValidateOptions<KafkaOptions>
{
    public ValidateOptionsResult Validate(string? name, KafkaOptions options)
    {
        var problems = new List<string>();

        if (string.IsNullOrWhiteSpace(options.BootstrapServers))
        {
            problems.Add("Kafka:BootstrapServers is required (comma-separated host:port list).");
        }

        if (string.IsNullOrWhiteSpace(options.ClientId))
        {
            problems.Add("Kafka:ClientId must not be empty.");
        }

        if (!KafkaOptions.TopicPrefixPattern().IsMatch(options.TopicPrefix ?? string.Empty))
        {
            problems.Add("Kafka:TopicPrefix must be 1-60 characters: letters, digits, '.', '_' or '-', starting with a letter or digit.");
        }

        if (options.ReplicationFactor < 1)
        {
            problems.Add("Kafka:ReplicationFactor must be at least 1.");
        }

        if (options.ProduceTimeoutSeconds is < 1 or > 300)
        {
            problems.Add("Kafka:ProduceTimeoutSeconds must be between 1 and 300.");
        }

        if (options.UsesSasl)
        {
            if (options.SaslMechanism is null)
            {
                problems.Add("Kafka:SaslMechanism is required when Kafka:SecurityProtocol is SaslPlaintext or SaslSsl.");
            }

            if (string.IsNullOrWhiteSpace(options.SaslUsername) || string.IsNullOrWhiteSpace(options.SaslPassword))
            {
                problems.Add("Kafka:SaslUsername and Kafka:SaslPassword are required when Kafka:SecurityProtocol is SaslPlaintext or SaslSsl.");
            }
        }

        return problems.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(problems);
    }
}
