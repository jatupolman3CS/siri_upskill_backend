using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Siri.Integrations.Email;

namespace Siri.UnitTests.Email;

public class SmtpEmailSenderOptionsTests
{
    [Theory]
    [InlineData("Development", true)]
    [InlineData("Production", false)]
    [InlineData("Staging", false)]
    public void UnencryptedCaptureServer_IsAllowedOnlyInDevelopment(string environment, bool allowed)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings
        {
            EnvironmentName = environment,
            DisableDefaults = true,
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Email:Provider"] = "Smtp",
            ["Email:Smtp:Host"] = "mailpit",
            ["Email:Smtp:FromAddress"] = "no-reply@example.test",
            ["Email:Smtp:AllowInsecure"] = "true",
        });
        builder.Services.AddEmailIntegration(builder.Configuration);
        using var host = builder.Build();
        var options = host.Services.GetRequiredService<IOptions<SmtpEmailSenderOptions>>();
        if (allowed)
        {
            Assert.True(options.Value.AllowInsecure);
        }
        else
        {
            Assert.Throws<OptionsValidationException>(() => options.Value);
        }
    }

    [Fact]
    public void Binding_FromEmailSmtpConfigurationSection_PopulatesEveryField()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Email:Smtp:Host"] = "smtp.example.com",
                ["Email:Smtp:Port"] = "2525",
                ["Email:Smtp:Username"] = "test-user",
                ["Email:Smtp:Password"] = "test-pass",
                ["Email:Smtp:FromAddress"] = "no-reply@example.com",
                ["Email:Smtp:FromDisplayName"] = "SIRI UpSkill Test",
                ["Email:Smtp:UseStartTls"] = "false",
            })
            .Build();

        var options = configuration.GetSection(SmtpEmailSenderOptions.SectionName).Get<SmtpEmailSenderOptions>();

        Assert.NotNull(options);
        Assert.Equal("smtp.example.com", options!.Host);
        Assert.Equal(2525, options.Port);
        Assert.Equal("test-user", options.Username);
        Assert.Equal("test-pass", options.Password);
        Assert.Equal("no-reply@example.com", options.FromAddress);
        Assert.Equal("SIRI UpSkill Test", options.FromDisplayName);
        Assert.False(options.UseStartTls);
    }

    [Fact]
    public void Binding_SectionMissing_LeavesDefaultsInPlace()
    {
        var configuration = new ConfigurationBuilder().Build();

        var options = configuration.GetSection(SmtpEmailSenderOptions.SectionName).Get<SmtpEmailSenderOptions>()
            ?? new SmtpEmailSenderOptions();

        Assert.Equal(587, options.Port);
        Assert.True(options.UseStartTls);
        Assert.Equal("SIRI UpSkill", options.FromDisplayName);
    }

    [Fact]
    public void SectionName_MatchesEmailSmtpConvention()
    {
        Assert.Equal("Email:Smtp", SmtpEmailSenderOptions.SectionName);
    }
}
