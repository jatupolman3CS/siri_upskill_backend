using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Siri.Api.Configuration;

namespace Siri.UnitTests.Security;

/// <summary>
/// The Production guard's Kafka rule: only a host that actually runs the notification pipeline needs a valid broker configuration,
/// and the API runs the pipeline exactly when it hosts the Hangfire server itself (<c>Hangfire:ServerInApi</c>, default true).
/// The guard collects every problem into one exception, so these tests look for the Kafka line among the others.
/// </summary>
public sealed class NotificationDeliveryGuardTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "Siri.Tests";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    private static string? KafkaProblemsFor(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();

        try
        {
            ProductionConfigurationGuard.ValidateProductionConfiguration(configuration, new Env("Production"));
            return null;
        }
        catch (InvalidOperationException ex)
        {
            // Only the lines about Kafka matter here; everything else in the bare configuration is (rightly) also wrong.
            var lines = ex.Message.Split('\n').Where(l => l.Contains("Kafka:", StringComparison.Ordinal)).ToList();
            return lines.Count == 0 ? null : string.Join("\n", lines);
        }
    }

    [Fact]
    public void ValidateProductionConfiguration_KafkaTransportWithoutBroker_ReportsKafkaProblems()
    {
        var problems = KafkaProblemsFor(("Notification:Delivery:Transport", "Kafka"));

        Assert.NotNull(problems);
        Assert.Contains("Kafka:BootstrapServers", problems, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateProductionConfiguration_KafkaTransportWithCleartextSasl_IsRefusedUntilAcknowledged()
    {
        var problems = KafkaProblemsFor(
            ("Notification:Delivery:Transport", "Kafka"),
            ("Kafka:BootstrapServers", "broker.example.com:9092"),
            ("Kafka:SecurityProtocol", "SaslPlaintext"));

        Assert.NotNull(problems);
        Assert.Contains("unencrypted", problems, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidateProductionConfiguration_KafkaTransportWithTlsBroker_HasNoKafkaProblems()
    {
        var problems = KafkaProblemsFor(
            ("Notification:Delivery:Transport", "Kafka"),
            ("Kafka:BootstrapServers", "broker.example.com:9093"),
            ("Kafka:SecurityProtocol", "SaslSsl"));

        Assert.Null(problems);
    }

    [Fact]
    public void ValidateProductionConfiguration_DatabaseTransport_NeverMentionsKafka()
    {
        Assert.Null(KafkaProblemsFor(("Notification:Delivery:Transport", "Database")));
    }

    [Fact]
    public void ValidateProductionConfiguration_NoTransportSetting_NeverMentionsKafka()
    {
        Assert.Null(KafkaProblemsFor());
    }

    [Fact]
    public void ValidateProductionConfiguration_KafkaTransportButJobServerLivesInWorkers_ApiDoesNotNeedTheBroker()
    {
        var problems = KafkaProblemsFor(
            ("Notification:Delivery:Transport", "Kafka"),
            ("Hangfire:ServerInApi", "false"));

        Assert.Null(problems);
    }
}
