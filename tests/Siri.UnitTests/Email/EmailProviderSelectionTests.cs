using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Siri.Integrations.Email;

namespace Siri.UnitTests.Email;

/// <summary>
/// Real data only: mail must never be silently dropped. <c>Email:Provider=Log</c> is an explicit opt-in
/// that delivers nothing; an unset provider fails every send loudly; an incomplete or misspelled
/// configuration fails the boot.
/// </summary>
public class EmailProviderSelectionTests
{
    private static ServiceProvider Build(Dictionary<string, string?> settings, string environment = "Development")
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddSingleton<IHostEnvironment>(new FakeEnvironment(environment));
        services.AddEmailIntegration(configuration);
        return services.BuildServiceProvider();
    }

    private static readonly EmailMessage Message = new("someone@example.test", "Subject", "<p>Body</p>");

    [Fact]
    public async Task ProviderUnset_RegistersUnconfiguredSender_ThatFailsEverySend()
    {
        using var provider = Build([]);

        var sender = provider.GetRequiredService<IEmailSender>();
        var result = await sender.SendAsync(Message, CancellationToken.None);

        Assert.IsType<UnconfiguredEmailSender>(sender);
        Assert.True(result.IsFailure);
        Assert.Equal(UnconfiguredEmailSender.ProviderNotConfiguredCode, result.Error.Code);
        Assert.EndsWith("_not_configured", result.Error.Code);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void ProviderBlank_IsTreatedAsUnset(string value)
    {
        using var provider = Build(new() { ["Email:Provider"] = value });

        Assert.IsType<UnconfiguredEmailSender>(provider.GetRequiredService<IEmailSender>());
    }

    [Theory]
    [InlineData("Log")]
    [InlineData("log")]
    [InlineData(" LOG ")]
    public async Task ProviderLog_IsAnExplicitOptIn_ThatSucceedsWithoutDelivering(string value)
    {
        using var provider = Build(new() { ["Email:Provider"] = value });

        var sender = provider.GetRequiredService<IEmailSender>();
        var result = await sender.SendAsync(Message, CancellationToken.None);

        Assert.IsType<LoggingEmailSender>(sender);
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public void ProviderSmtp_RegistersTheRealSender()
    {
        using var provider = Build(new()
        {
            ["Email:Provider"] = "Smtp",
            ["Email:Smtp:Host"] = "smtp.example.test",
            ["Email:Smtp:FromAddress"] = "no-reply@example.test",
        });

        Assert.IsType<SmtpEmailSender>(provider.GetRequiredService<IEmailSender>());
    }

    [Fact]
    public void ProviderSmtp_WithoutHost_FailsOptionsValidation_InsteadOfFallingBackToLog()
    {
        // Previously: Provider=Smtp + empty Host silently registered the no-op logger.
        using var provider = Build(new()
        {
            ["Email:Provider"] = "Smtp",
            ["Email:Smtp:Host"] = "",
            ["Email:Smtp:FromAddress"] = "no-reply@example.test",
        });

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<SmtpEmailSenderOptions>>().Value);
    }

    [Theory]
    [InlineData("SendGrid")]
    [InlineData("Smtp2")]
    [InlineData("none")]
    public void ProviderUnknown_ThrowsAtRegistration(string value)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Build(new() { ["Email:Provider"] = value }));

        Assert.Contains("Email:Provider", ex.Message);
        Assert.Contains(value, ex.Message);
    }

    [Fact]
    public async Task UnconfiguredSender_NeverReportsSuccess()
    {
        var sender = new UnconfiguredEmailSender(NullLogger<UnconfiguredEmailSender>.Instance);

        var result = await sender.SendAsync(Message, CancellationToken.None);

        Assert.True(result.IsFailure);
    }

    // ---- EmailProductionRequirements (shared by the API guard and the Workers host) ----

    private static Dictionary<string, string?> RealSmtp() => new()
    {
        ["Email:Provider"] = "Smtp",
        ["Email:Smtp:Host"] = "smtp.siriupskill.com",
        ["Email:Smtp:FromAddress"] = "no-reply@siriupskill.com",
    };

    private static IReadOnlyList<string> Problems(Dictionary<string, string?> settings) =>
        EmailProductionRequirements.GetProblems(new ConfigurationBuilder().AddInMemoryCollection(settings).Build());

    [Fact]
    public void ProductionRequirements_RealSmtp_HasNoProblems()
    {
        Assert.Empty(Problems(RealSmtp()));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("Log")]
    [InlineData("Other")]
    public void ProductionRequirements_ProviderNotSmtp_IsAProblem(string? provider)
    {
        var settings = RealSmtp();
        settings["Email:Provider"] = provider;

        var problem = Assert.Single(Problems(settings));
        Assert.Contains("Email:Provider must be 'Smtp'", problem);
    }

    [Fact]
    public void ProductionRequirements_MissingHost_IsAProblem()
    {
        var settings = RealSmtp();
        settings["Email:Smtp:Host"] = null;

        Assert.Contains(Problems(settings), p => p.Contains("Email:Smtp:Host"));
    }

    [Theory]
    [InlineData("no-reply@example.com")]
    [InlineData("no-reply@example.net")]
    [InlineData("a@b.test")]
    [InlineData("a@b.invalid")]
    [InlineData("a@mail.localhost")]
    [InlineData("not-an-address")]
    public void ProductionRequirements_FakeFromAddress_IsAProblem(string from)
    {
        var settings = RealSmtp();
        settings["Email:Smtp:FromAddress"] = from;

        Assert.Contains(Problems(settings), p => p.Contains("Email:Smtp:FromAddress"));
    }

    [Fact]
    public void ProductionRequirements_AllowInsecure_IsAProblem()
    {
        var settings = RealSmtp();
        settings["Email:Smtp:AllowInsecure"] = "true";

        Assert.Contains(Problems(settings), p => p.Contains("AllowInsecure"));
    }

    private sealed class FakeEnvironment(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "tests";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
