using Confluent.Kafka;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Siri.Integrations.Messaging;
using Siri.Modules.Notification.Infrastructure.Delivery;
using Siri.SharedKernel.Configuration;

namespace Siri.UnitTests.Configuration;

public sealed class DotEnvConfigurationExtensionsTests : IDisposable
{
    private readonly string _directory = Directory.CreateTempSubdirectory("SiriDotEnvTests-").FullName;

    [Fact]
    public void Defaults_OverrideAppSettings_ButPreserveEnvironmentAndCommandLineOverrides()
    {
        var key = $"SiriDotEnvTest{Guid.NewGuid():N}";
        var environmentKey = $"{key}__Environment";
        File.WriteAllText(Path.Combine(_directory, "appsettings.json"),
            $$$"""{"{{{key}}}":{"Default":"json","Environment":"json","CommandLine":"json"}}""");
        File.WriteAllText(Path.Combine(_directory, ".env"),
            $"{key}__Default=dotenv\n{environmentKey}=dotenv\n{key}__CommandLine=dotenv");
        Environment.SetEnvironmentVariable(environmentKey, "operator");

        try
        {
            var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
            {
                ApplicationName = typeof(DotEnvConfigurationExtensionsTests).Assembly.GetName().Name,
                ContentRootPath = _directory,
                EnvironmentName = Environments.Development,
                Args = [$"--{key}:CommandLine=cli"],
            });

            builder.Configuration.AddSiriDotEnvDefaults(builder.Environment, _directory);

            Assert.Equal("dotenv", builder.Configuration[$"{key}:Default"]);
            Assert.Equal("operator", builder.Configuration[$"{key}:Environment"]);
            Assert.Equal("cli", builder.Configuration[$"{key}:CommandLine"]);
            Assert.Equal("operator", Environment.GetEnvironmentVariable(environmentKey));
        }
        finally
        {
            Environment.SetEnvironmentVariable(environmentKey, null);
        }
    }

    [Fact]
    public void Defaults_PreserveUserSecretJsonProviderOverrides()
    {
        File.WriteAllText(Path.Combine(_directory, "appsettings.json"), """{"Settings":{"Value":"json"}}""");
        File.WriteAllText(Path.Combine(_directory, "secrets.json"), """{"Settings":{"Value":"user-secret"}}""");
        File.WriteAllText(Path.Combine(_directory, ".env"), "Settings__Value=dotenv");

        var configuration = new ConfigurationBuilder()
            .SetBasePath(_directory)
            .AddJsonFile("appsettings.json")
            .AddJsonFile("secrets.json")
            .AddSiriDotEnvDefaults(new TestEnvironment(Environments.Development, _directory), _directory)
            .Build();

        Assert.Equal("user-secret", configuration["Settings:Value"]);
    }

    [Fact]
    public void Development_LoadsOnlyDotEnv()
    {
        File.WriteAllText(Path.Combine(_directory, ".env"), "Settings__Value=base\nSettings__BaseOnly=base");
        File.WriteAllText(Path.Combine(_directory, ".env.local"), "Settings__Value=local\nSettings__LocalOnly=local");
        File.WriteAllText(Path.Combine(_directory, ".env.development"), "Settings__Value=development");
        File.WriteAllText(Path.Combine(_directory, ".env.development.local"), "Settings__Value=development-local");
        File.WriteAllText(Path.Combine(_directory, ".env.production"), "Settings__Value=production");

        var configuration = Load(Environments.Development);

        Assert.Equal("base", configuration["Settings:Value"]);
        Assert.Equal("base", configuration["Settings:BaseOnly"]);
        Assert.Null(configuration["Settings:LocalOnly"]);
    }

    [Fact]
    public void Qa_LoadsOnlyDotEnv()
    {
        File.WriteAllText(Path.Combine(_directory, ".env"), "Settings__Value=base");
        File.WriteAllText(Path.Combine(_directory, ".env.local"), "Settings__Value=local");
        File.WriteAllText(Path.Combine(_directory, ".env.development"), "Settings__Value=development");
        File.WriteAllText(Path.Combine(_directory, ".env.production"), "Settings__Value=production");

        var configuration = Load("QA");

        Assert.Equal("base", configuration["Settings:Value"]);
    }

    [Fact]
    public void ProductionFiles_UseProductionOverride_AndIgnoreDevelopmentDefaults()
    {
        File.WriteAllText(Path.Combine(_directory, ".env"), "Settings__DevelopmentOnly=development");
        File.WriteAllText(Path.Combine(_directory, ".env_prd"), "Settings__Value=legacy\nSettings__LegacyOnly=legacy");
        File.WriteAllText(Path.Combine(_directory, ".env.production"), "Settings__Value=production");

        var configuration = Load(Environments.Production);

        Assert.Equal("production", configuration["Settings:Value"]);
        Assert.Equal("legacy", configuration["Settings:LegacyOnly"]);
        Assert.Null(configuration["Settings:DevelopmentOnly"]);
    }

    [Fact]
    public void DevelopmentAndQa_DoNotFallBackToProductionFiles()
    {
        var key = $"ProductionOnly{Guid.NewGuid():N}";
        File.WriteAllText(Path.Combine(_directory, ".env_prd"), $"{key}=legacy");
        File.WriteAllText(Path.Combine(_directory, ".env.production"), $"{key}=production");

        Assert.Null(Load(Environments.Development)[key]);
        Assert.Null(Load("QA")[key]);
    }

    [Theory]
    [InlineData("IntegrationTest")]
    [InlineData("Testing")]
    [InlineData("Staging")]
    public void NonDevelopmentAndProductionHosts_DoNotDiscoverDotEnvFiles(string environmentName)
    {
        File.WriteAllText(Path.Combine(_directory, ".env"), "Settings__Value=development");
        File.WriteAllText(Path.Combine(_directory, ".env.production"), "Settings__Value=production");
        var builder = new ConfigurationBuilder();

        builder.AddSiriDotEnvDefaults(new TestEnvironment(environmentName, _directory), _directory);

        Assert.Empty(builder.Sources);
        Assert.Null(builder.Build()["Settings:Value"]);
    }

    [Fact]
    public void LoadingDotEnv_DoesNotSetProcessEnvironmentVariables()
    {
        var key = $"SiriDotEnvTest{Guid.NewGuid():N}__Value";
        File.WriteAllText(Path.Combine(_directory, ".env"), $"{key}=local-only");

        var configuration = Load(Environments.Development);

        Assert.Equal("local-only", configuration[key.Replace("__", ":", StringComparison.Ordinal)]);
        Assert.Null(Environment.GetEnvironmentVariable(key));
        Assert.Null(Environment.GetEnvironmentVariable(key.Replace("__", ":", StringComparison.Ordinal)));
    }

    [Fact]
    public void Parser_PreservesLiteralValues_AndHandlesQuotesCommentsAndNestedKeys()
    {
        File.WriteAllText(Path.Combine(_directory, ".env"), """
            # comment
            invalid line
            export Settings__Connection = 'Host=localhost;Password=contains=equals#hash' # comment
            Settings__Quoted = "a # b" # comment
            Settings__Inline = value # comment
            Settings__Hash = value#literal
            Settings__Literal = ${NO_INTERPOLATION}
            Settings__Empty =
            """);

        var configuration = Load(Environments.Development);

        Assert.Equal("Host=localhost;Password=contains=equals#hash", configuration["Settings:Connection"]);
        Assert.Equal("a # b", configuration["Settings:Quoted"]);
        Assert.Equal("value", configuration["Settings:Inline"]);
        Assert.Equal("value#literal", configuration["Settings:Hash"]);
        Assert.Equal("${NO_INTERPOLATION}", configuration["Settings:Literal"]);
        Assert.Equal(string.Empty, configuration["Settings:Empty"]);
    }

    [Theory]
    [InlineData(".env", "Development")]
    [InlineData(".env_prd", "Production")]
    public void KafkaAndDeliveryKeys_FromDotEnvFile_BindToTheirOptions(string fileName, string environmentName)
    {
        File.WriteAllText(Path.Combine(_directory, fileName), """
            Notification__Delivery__Transport=Kafka
            Kafka__BootstrapServers=broker-1:9092,broker-2:9092
            Kafka__TopicPrefix=siriupskill-test
            Kafka__ReplicationFactor=3
            Kafka__SecurityProtocol=SaslSsl
            Kafka__SaslMechanism=ScramSha512
            Kafka__SaslUsername=app
            Kafka__SaslPassword=CHANGE_ME_test
            """);

        var configuration = Load(environmentName);
        var kafka = configuration.GetSection(KafkaOptions.SectionName).Get<KafkaOptions>();
        var delivery = configuration.GetSection(NotificationDeliveryOptions.SectionName).Get<NotificationDeliveryOptions>();

        Assert.NotNull(kafka);
        Assert.Equal("broker-1:9092,broker-2:9092", kafka.BootstrapServers);
        Assert.Equal("siriupskill-test", kafka.TopicPrefix);
        Assert.Equal(3, kafka.ReplicationFactor);
        Assert.Equal(SecurityProtocol.SaslSsl, kafka.SecurityProtocol);
        Assert.Equal(SaslMechanism.ScramSha512, kafka.SaslMechanism);
        Assert.Equal("app", kafka.SaslUsername);
        Assert.Equal("CHANGE_ME_test", kafka.SaslPassword);
        Assert.NotNull(delivery);
        Assert.Equal(NotificationTransport.Kafka, delivery.Transport);
    }

    [Fact]
    public void Discovery_UsesNearestAncestorWithMatchingFiles()
    {
        var projectDirectory = Directory.CreateDirectory(Path.Combine(_directory, "project")).FullName;
        var contentDirectory = Directory.CreateDirectory(Path.Combine(projectDirectory, "src", "api")).FullName;
        File.WriteAllText(Path.Combine(_directory, ".env"), "Settings__Value=ancestor\nSettings__AncestorOnly=ancestor");
        File.WriteAllText(Path.Combine(projectDirectory, ".env"), "Settings__Value=project");

        var configuration = new ConfigurationBuilder()
            .AddSiriDotEnvDefaults(new TestEnvironment(Environments.Development, contentDirectory), contentDirectory)
            .Build();

        Assert.Equal("project", configuration["Settings:Value"]);
        Assert.Null(configuration["Settings:AncestorOnly"]);
    }

    private IConfigurationRoot Load(string environmentName) => new ConfigurationBuilder()
        .AddSiriDotEnvDefaults(new TestEnvironment(environmentName, _directory), _directory)
        .Build();

    public void Dispose() => Directory.Delete(_directory, recursive: true);

    private sealed class TestEnvironment(string environmentName, string contentRoot) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "DotEnvTests";
        public string ContentRootPath { get; set; } = contentRoot;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }
}
