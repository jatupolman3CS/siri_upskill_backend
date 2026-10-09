using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Siri.Integrations.Email;

namespace Siri.UnitTests.Email;

/// <summary>The registered fact "which e-mail provider is this process using", so diagnostics never re-read configuration or sniff the sender's type.</summary>
public class EmailProviderInfoTests
{
    private sealed class Env : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = "Development";

        public string ApplicationName { get; set; } = "test";

        public string ContentRootPath { get; set; } = ".";

        public IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private static EmailProviderInfo Info(params (string Key, string Value)[] settings)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(settings.ToDictionary(s => s.Key, s => (string?)s.Value))
            .Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new Env());
        services.AddEmailIntegration(configuration);
        using var provider = services.BuildServiceProvider();
        return provider.GetRequiredService<EmailProviderInfo>();
    }

    [Fact]
    public void Smtp_IsReportedAsSmtp_AndDeliversMail()
    {
        var info = Info(("Email:Provider", "Smtp"), ("Email:Smtp:Host", "smtp.example.test"), ("Email:Smtp:FromAddress", "no-reply@example.test"));

        Assert.Equal("Smtp", info.Name);
        Assert.True(info.DeliversMail);
    }

    [Theory]
    [InlineData("Log")]
    [InlineData("log")]
    public void Log_IsReportedAsLog_AndDeliversNothing(string value)
    {
        var info = Info(("Email:Provider", value));

        Assert.Equal("Log", info.Name);
        Assert.False(info.DeliversMail);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Unset_IsReportedAsUnconfigured(string value)
    {
        var info = Info(("Email:Provider", value));

        Assert.Equal("Unconfigured", info.Name);
        Assert.False(info.DeliversMail);
    }

    [Fact]
    public void NoSettingAtAll_IsReportedAsUnconfigured() => Assert.Equal("Unconfigured", Info().Name);

    [Fact]
    public void TheInfo_CarriesNoSettingValues()
    {
        var info = Info(("Email:Provider", "Smtp"), ("Email:Smtp:Host", "secret-host.internal"), ("Email:Smtp:Password", "hunter2"), ("Email:Smtp:FromAddress", "no-reply@example.test"));

        Assert.DoesNotContain("secret-host", info.ToString());
        Assert.DoesNotContain("hunter2", info.ToString());
    }
}
