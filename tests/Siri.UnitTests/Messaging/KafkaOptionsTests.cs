using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Siri.Integrations.Messaging;

namespace Siri.UnitTests.Messaging;

public class KafkaOptionsTests
{
    private static KafkaOptions Valid() => new() { BootstrapServers = "siri_kafka:9092", AllowPlaintext = true };

    private sealed record ValidationOutcome(bool Succeeded, IReadOnlyList<string> Failures);

    private static ValidationOutcome Validate(KafkaOptions options)
    {
        var result = new KafkaOptionsValidator().Validate(name: null, options);
        return new ValidationOutcome(result.Succeeded, result.Failures?.ToList() ?? []);
    }

    [Fact]
    public void Validate_MinimalPlaintextConfiguration_Succeeds()
    {
        Assert.True(Validate(Valid()).Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Validate_BlankBootstrapServers_Fails(string bootstrapServers)
    {
        var options = Valid();
        options.BootstrapServers = bootstrapServers;

        var outcome = Validate(options);

        Assert.False(outcome.Succeeded);
        Assert.Contains(outcome.Failures, f => f.Contains("BootstrapServers", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("siriupskill")]
    [InlineData("siriupskill-dev")]
    [InlineData("a.b_c-d9")]
    public void Validate_TopicPrefixOfAllowedCharacters_Succeeds(string prefix)
    {
        var options = Valid();
        options.TopicPrefix = prefix;

        Assert.True(Validate(options).Succeeded);
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("has space")]
    [InlineData("-leading-dash")]
    [InlineData("slash/ed")]
    [InlineData("ไทย")]
    public void Validate_TopicPrefixOfIllegalCharacters_Fails(string prefix)
    {
        var options = Valid();
        options.TopicPrefix = prefix;

        var outcome = Validate(options);

        Assert.False(outcome.Succeeded);
        Assert.Contains(outcome.Failures, f => f.Contains("TopicPrefix", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_TopicPrefixLongerThanSixtyCharacters_Fails()
    {
        var options = Valid();
        options.TopicPrefix = new string('a', 61);

        Assert.False(Validate(options).Succeeded);
    }

    [Fact]
    public void Validate_ReplicationFactorBelowOne_Fails()
    {
        var options = Valid();
        options.ReplicationFactor = 0;

        Assert.False(Validate(options).Succeeded);
    }

    [Fact]
    public void Defaults_UseAnApplicationSpecificPrefixAndASingleReplica()
    {
        var options = new KafkaOptions();

        Assert.Equal("siriupskill", options.TopicPrefix);
        Assert.Equal(1, options.ReplicationFactor);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(301)]
    public void Validate_ProduceTimeoutOutsideRange_Fails(int seconds)
    {
        var options = Valid();
        options.ProduceTimeoutSeconds = seconds;

        Assert.False(Validate(options).Succeeded);
    }

    [Theory]
    [InlineData(SecurityProtocol.SaslPlaintext)]
    [InlineData(SecurityProtocol.SaslSsl)]
    public void Validate_SaslWithoutMechanismOrCredentials_Fails(SecurityProtocol protocol)
    {
        var options = Valid();
        options.SecurityProtocol = protocol;

        var outcome = Validate(options);

        Assert.False(outcome.Succeeded);
        Assert.Contains(outcome.Failures, f => f.Contains("SaslMechanism", StringComparison.Ordinal));
        Assert.Contains(outcome.Failures, f => f.Contains("SaslUsername", StringComparison.Ordinal));
    }

    [Fact]
    public void Validate_SaslSslWithMechanismAndCredentials_Succeeds()
    {
        var options = Valid();
        options.SecurityProtocol = SecurityProtocol.SaslSsl;
        options.SaslMechanism = SaslMechanism.ScramSha512;
        options.SaslUsername = "siri";
        options.SaslPassword = "from-a-secret-store";

        Assert.True(Validate(options).Succeeded);
    }

    [Fact]
    public void Validate_FailureMessages_NeverContainThePassword()
    {
        var options = Valid();
        options.SecurityProtocol = SecurityProtocol.SaslSsl;
        options.SaslPassword = "hunter2-do-not-leak";

        var outcome = Validate(options);

        Assert.DoesNotContain(outcome.Failures, f => f.Contains("hunter2", StringComparison.Ordinal));
    }

    [Fact]
    public void Binding_FromConfiguration_ReadsEnumsAndFlags()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Kafka:BootstrapServers"] = "a:9092,b:9092",
                ["Kafka:SecurityProtocol"] = "SaslSsl",
                ["Kafka:SaslMechanism"] = "ScramSha256",
                ["Kafka:AllowPlaintext"] = "true",
            })
            .Build();

        var options = configuration.GetSection(KafkaOptions.SectionName).Get<KafkaOptions>()!;

        Assert.Equal("a:9092,b:9092", options.BootstrapServers);
        Assert.Equal(SecurityProtocol.SaslSsl, options.SecurityProtocol);
        Assert.Equal(SaslMechanism.ScramSha256, options.SaslMechanism);
        Assert.True(options.AllowPlaintext);
    }
}

public class KafkaProductionRequirementsTests
{
    private static IConfiguration Config(params (string Key, string Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.ToDictionary(v => v.Key, v => (string?)v.Value))
            .Build();

    [Fact]
    public void GetProblems_NothingConfigured_ReportsMissingBrokerAndPlaintext()
    {
        var problems = KafkaProductionRequirements.GetProblems(Config());

        Assert.Contains(problems, p => p.Contains("BootstrapServers", StringComparison.Ordinal));
        Assert.Contains(problems, p => p.Contains("Plaintext", StringComparison.Ordinal));
    }

    [Fact]
    public void GetProblems_PlaintextAcknowledged_IsAccepted()
    {
        var problems = KafkaProductionRequirements.GetProblems(Config(
            ("Kafka:BootstrapServers", "siri_kafka:9092"),
            ("Kafka:AllowPlaintext", "true")));

        Assert.Empty(problems);
    }

    [Fact]
    public void GetProblems_SaslOverPlaintext_StillCountsAsUnencryptedBecauseThePasswordTravelsInTheClear()
    {
        var problems = KafkaProductionRequirements.GetProblems(Config(
            ("Kafka:BootstrapServers", "broker.example.com:9092"),
            ("Kafka:SecurityProtocol", "SaslPlaintext")));

        Assert.Contains(problems, p => p.Contains("unencrypted", StringComparison.Ordinal));
    }

    [Fact]
    public void GetProblems_SaslOverPlaintextAcknowledged_IsAccepted()
    {
        var problems = KafkaProductionRequirements.GetProblems(Config(
            ("Kafka:BootstrapServers", "siri_kafka:9092"),
            ("Kafka:SecurityProtocol", "SaslPlaintext"),
            ("Kafka:AllowPlaintext", "true")));

        Assert.Empty(problems);
    }

    [Fact]
    public void GetProblems_TlsBroker_IsAcceptedWithoutTheAcknowledgement()
    {
        var problems = KafkaProductionRequirements.GetProblems(Config(
            ("Kafka:BootstrapServers", "broker.example.com:9093"),
            ("Kafka:SecurityProtocol", "SaslSsl")));

        Assert.Empty(problems);
    }

    [Fact]
    public void GetProblems_NeverEchoesSecrets()
    {
        var problems = KafkaProductionRequirements.GetProblems(Config(
            ("Kafka:SaslPassword", "super-secret-value")));

        Assert.DoesNotContain(problems, p => p.Contains("super-secret-value", StringComparison.Ordinal));
    }
}
