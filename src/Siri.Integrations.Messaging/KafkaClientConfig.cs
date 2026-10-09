using Confluent.Kafka;

namespace Siri.Integrations.Messaging;

internal static class KafkaClientConfig
{
    /// <summary>Copies connection + security settings onto a producer/consumer/admin config. Never logs or echoes the password.</summary>
    public static T Apply<T>(T config, KafkaOptions options, string clientIdSuffix)
        where T : ClientConfig
    {
        config.BootstrapServers = options.BootstrapServers;
        config.ClientId = $"{options.ClientId}-{clientIdSuffix}";
        config.SecurityProtocol = options.SecurityProtocol;

        if (options.UsesSasl)
        {
            config.SaslMechanism = options.SaslMechanism;
            config.SaslUsername = options.SaslUsername;
            config.SaslPassword = options.SaslPassword;
        }

        if (!string.IsNullOrWhiteSpace(options.SslCaLocation))
        {
            config.SslCaLocation = options.SslCaLocation;
        }

        return config;
    }
}
