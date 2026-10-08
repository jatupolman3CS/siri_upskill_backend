using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Siri.Api.Configuration;
using Siri.SharedKernel.Configuration;
using Siri.Workers;

namespace Siri.UnitTests.Security;

/// <summary>
/// D1 (integrator-qa): the DataProtection encryption-key production checks live in one shared helper used by BOTH hosts. Siri.Workers' own
/// appsettings.json ships the dev key and its production guard used to skip this check — so a Workers container started without the real key
/// booted, could not decrypt what the API encrypted, and revoked every instructor's Google connection.
/// </summary>
public sealed class DataProtectionProductionRequirementsTests
{
    private static string RealKey() => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    private static IConfiguration Config(params (string Key, string? Value)[] settings) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => s.Value))
            .Build();

    private static IConfiguration KeyConfig(string? key) => Config((DataProtectionProductionRequirements.ConfigurationKey, key));

    // ---- The shared helper ---------------------------------------------------------------------------

    [Fact]
    public void GetProblems_ARealRandom32ByteKey_IsAccepted()
    {
        Assert.Empty(DataProtectionProductionRequirements.GetProblems(KeyConfig(RealKey())));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(DataProtectionProductionRequirements.DevKeyPlaceholder)]
    public void GetProblems_MissingOrPlaceholderKey_IsRefused(string? key)
    {
        var problems = DataProtectionProductionRequirements.GetProblems(KeyConfig(key));

        var problem = Assert.Single(problems);
        Assert.Contains("DataProtection:EncryptionKeyBase64", problem);
        Assert.Contains("real key", problem);
    }

    [Theory]
    [InlineData(DataProtectionProductionRequirements.ShippedDevelopmentKeyBase64)]
    [InlineData(" " + DataProtectionProductionRequirements.ShippedDevelopmentKeyBase64 + "\n")]
    public void GetProblems_TheShippedDevelopmentKey_IsRefused_EvenWithWhitespace(string key)
    {
        var problems = DataProtectionProductionRequirements.GetProblems(KeyConfig(key));

        Assert.Contains(problems, p => p.Contains("shipped development key"));
    }

    [Fact]
    public void GetProblems_InvalidBase64_IsRefused()
    {
        var problems = DataProtectionProductionRequirements.GetProblems(KeyConfig("this is not base64 !!!"));

        Assert.Contains(problems, p => p.Contains("not a valid Base64"));
    }

    [Theory]
    [InlineData(16)]
    [InlineData(31)]
    [InlineData(33)]
    [InlineData(64)]
    public void GetProblems_WrongKeyLength_IsRefused(int byteCount)
    {
        var problems = DataProtectionProductionRequirements.GetProblems(KeyConfig(Convert.ToBase64String(RandomNumberGenerator.GetBytes(byteCount))));

        Assert.Contains(problems, p => p.Contains("exactly 32 bytes") && p.Contains($"Got {byteCount} bytes"));
    }

    [Fact]
    public void GetProblems_NeverEchoesTheKey()
    {
        var key = RealKey()[..^1] + "$"; // invalid base64 — the message must still not repeat what was configured
        var problems = DataProtectionProductionRequirements.GetProblems(KeyConfig(key));

        Assert.NotEmpty(problems);
        Assert.DoesNotContain(problems, p => p.Contains(key));
    }

    // ---- Siri.Workers uses it ------------------------------------------------------------------------

    private static IEnumerable<(string Key, string? Value)> OtherwiseValidWorkersConfig() =>
    [
        ("Email:Provider", "Smtp"),
        ("Email:Smtp:Host", "smtp.siriupskill.com"),
        ("Email:Smtp:FromAddress", "no-reply@siriupskill.com"),
        ("Seo:PublicBaseUrl", "https://app.siriupskill.com"),
    ];

    private static IConfiguration WorkersConfig(string? encryptionKey) =>
        Config([.. OtherwiseValidWorkersConfig(), (DataProtectionProductionRequirements.ConfigurationKey, encryptionKey)]);

    [Fact]
    public void Workers_WithTheShippedDevKey_IsRefused_EvenWhenEmailAndLiveAreFine()
    {
        // This is exactly the Workers container that was started without DataProtection__EncryptionKeyBase64: it falls back to its own
        // appsettings.json, which carries the shipped dev key.
        var problems = WorkersProductionRequirements.GetProblems(WorkersConfig(DataProtectionProductionRequirements.ShippedDevelopmentKeyBase64));

        var problem = Assert.Single(problems);
        Assert.Contains("DataProtection:EncryptionKeyBase64", problem);
        Assert.Contains("shipped development key", problem);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(DataProtectionProductionRequirements.DevKeyPlaceholder)]
    public void Workers_WithAMissingOrPlaceholderKey_IsRefused(string? key)
    {
        var problems = WorkersProductionRequirements.GetProblems(WorkersConfig(key));

        Assert.Contains(problems, p => p.Contains("DataProtection:EncryptionKeyBase64"));
    }

    [Fact]
    public void Workers_WithARealKey_AndValidEmailAndLive_IsAccepted()
    {
        Assert.Empty(WorkersProductionRequirements.GetProblems(WorkersConfig(RealKey())));
    }

    [Fact]
    public void Workers_StillEnforcesTheEmailAndLiveChecks()
    {
        var problems = WorkersProductionRequirements.GetProblems(Config(
            (DataProtectionProductionRequirements.ConfigurationKey, RealKey()),
            ("Live:Provider", "Logging")));

        Assert.Contains(problems, p => p.Contains("Email:Provider"));
        Assert.Contains(problems, p => p.Contains("Live:Provider"));
        Assert.DoesNotContain(problems, p => p.Contains("DataProtection"));
    }

    // ---- The API guard still reports the same messages (it now delegates to the helper) ---------------

    [Fact]
    public void ApiGuard_StillRefusesTheShippedDevKey_WithTheSameMessage()
    {
        var exception = Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationGuard.ValidateProductionConfiguration(
                KeyConfig(DataProtectionProductionRequirements.ShippedDevelopmentKeyBase64), new FakeEnvironment("Production")));

        Assert.Contains("DataProtection:EncryptionKeyBase64 must be replaced with a secure production key; the shipped development key is not allowed.", exception.Message);
    }

    private sealed class FakeEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Siri.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
