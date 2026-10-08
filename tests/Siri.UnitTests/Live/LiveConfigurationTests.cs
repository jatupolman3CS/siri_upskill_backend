using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Siri.Api.Configuration;
using Siri.Integrations.Google;
using Siri.Modules.Live;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Infrastructure;

namespace Siri.UnitTests.Live;

/// <summary>Live:* options (defaults, validation, fall-back to Seo:PublicBaseUrl), module registration, and the production guard additions.</summary>
public class LiveConfigurationTests
{
    private static IConfiguration Config(params (string Key, string? Value)[] values) =>
        new ConfigurationBuilder()
            .AddInMemoryCollection(values.Select(v => new KeyValuePair<string, string?>(v.Key, v.Value)))
            .Build();

    // ---- Options -----------------------------------------------------------------------------------

    [Fact]
    public void Options_Defaults_MatchTheContract()
    {
        var options = new LiveOptions();

        Assert.Equal(LiveProviderMode.GoogleMeet, options.Provider);
        Assert.Equal(15, options.JoinWindowBeforeMinutes);
        Assert.Equal(180, options.InviteLookaheadDays);
        Assert.Equal(150, options.GoogleAttendeeCap);
        Assert.Equal(["meet.google.com", "zoom.us", "teams.microsoft.com", "teams.live.com"], options.GetEffectiveAllowedMeetingHosts());
    }

    [Fact]
    public void Options_PublicBaseUrl_FallsBackToSeoAndOrganizerEmailIsDerivedFromItsHost()
    {
        var options = new LiveOptions();

        LiveOptions.ApplyDefaults(options, "https://www.example.org/");

        Assert.Equal("https://www.example.org/", options.PublicBaseUrl);
        Assert.Equal("https://www.example.org", options.GetNormalizedPublicBaseUrl());
        Assert.Equal("no-reply@www.example.org", options.OrganizerEmail);
    }

    [Fact]
    public void Options_ExplicitValues_AreNotOverriddenByTheFallback()
    {
        var options = new LiveOptions { PublicBaseUrl = "https://live.example.org", OrganizerEmail = "classes@example.org" };

        LiveOptions.ApplyDefaults(options, "https://seo.example.org");

        Assert.Equal("https://live.example.org", options.PublicBaseUrl);
        Assert.Equal("classes@example.org", options.OrganizerEmail);
    }

    [Fact]
    public void Options_BoundFromConfiguration_AllowedHostsReplaceTheDefaultsInsteadOfAppendingToThem()
    {
        var configuration = Config(("Live:AllowedMeetingHosts:0", "meet.example.org"));

        var options = configuration.GetSection("Live").Get<LiveOptions>()!;

        Assert.Equal(["meet.example.org"], options.GetEffectiveAllowedMeetingHosts());
    }

    [Theory]
    [InlineData("https://app.example.test", true)]
    [InlineData("http://localhost:4202", true)]
    [InlineData("http://127.0.0.1:4200", true)]
    [InlineData("http://app.example.test", false)]
    [InlineData("app.example.test", false)]
    [InlineData("", false)]
    [InlineData("javascript:alert(1)", false)]
    public void Validator_PublicBaseUrl_MustBeHttpsOrLoopback(string value, bool valid)
    {
        var result = new LiveOptionsValidator().Validate(null, new LiveOptions { PublicBaseUrl = value });

        Assert.Equal(valid, result.Succeeded);
    }

    [Theory]
    [InlineData("zoom.us")]
    [InlineData("https://zoom.us")]
    [InlineData("zoom.us/path")]
    [InlineData("*.zoom.us")]
    [InlineData("zoom.us:8443")]
    [InlineData("bad host")]
    public void Validator_AllowedHosts_MustBeBareDnsNames(string host)
    {
        var options = new LiveOptions { PublicBaseUrl = "https://app.example.test", AllowedMeetingHosts = [host] };

        var result = new LiveOptionsValidator().Validate(null, options);

        Assert.Equal(host == "zoom.us", result.Succeeded);
    }

    [Fact]
    public void Validator_OrganizerEmail_MustBeAnAddress()
    {
        var valid = new LiveOptionsValidator().Validate(null, new LiveOptions { PublicBaseUrl = "https://app.example.test", OrganizerEmail = "no-reply@app.example.test" });
        var invalid = new LiveOptionsValidator().Validate(null, new LiveOptions { PublicBaseUrl = "https://app.example.test", OrganizerEmail = "not an email" });

        Assert.True(valid.Succeeded);
        Assert.False(invalid.Succeeded);
    }

    [Theory]
    [InlineData(4)]
    [InlineData(121)]
    public void Options_JoinWindow_IsRangeChecked(int minutes)
    {
        var options = new LiveOptions { JoinWindowBeforeMinutes = minutes };
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        var valid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            options, new System.ComponentModel.DataAnnotations.ValidationContext(options), results, validateAllProperties: true);

        Assert.False(valid);
    }

    [Fact]
    public void Options_GoogleAttendeeCap_CannotExceedGoogleLimit()
    {
        var options = new LiveOptions { GoogleAttendeeCap = 191 };
        var results = new List<System.ComponentModel.DataAnnotations.ValidationResult>();

        var valid = System.ComponentModel.DataAnnotations.Validator.TryValidateObject(
            options, new System.ComponentModel.DataAnnotations.ValidationContext(options), results, validateAllProperties: true);

        Assert.False(valid);
    }

    // ---- Module registration ---------------------------------------------------------------------------

    [Fact]
    public void AddLiveModule_RegistersTheRealSinkAndReadinessReader_OverCatalogsNullDefaults_InEitherOrder()
    {
        var configuration = Config(("Seo:PublicBaseUrl", "https://app.example.test"));

        foreach (var liveFirst in new[] { true, false })
        {
            var services = new ServiceCollection();
            if (liveFirst)
            {
                services.AddLiveModule(configuration);
                AddCatalogNullDefaults(services);
            }
            else
            {
                AddCatalogNullDefaults(services);
                services.AddLiveModule(configuration);
            }

            var sink = services.Last(d => d.ServiceType == typeof(Siri.Modules.Catalog.Contracts.ILiveMeetingSink));
            var readiness = services.Last(d => d.ServiceType == typeof(Siri.Modules.Catalog.Contracts.ILiveMeetingReadinessReader));

            if (liveFirst)
            {
                // Catalog's TryAdd* saw the existing registration and added nothing: only Live's remain.
                Assert.Equal(typeof(LiveMeetingSink), sink.ImplementationType);
                Assert.Equal(typeof(LiveMeetingReadinessReader), readiness.ImplementationType);
                Assert.Single(services, d => d.ServiceType == typeof(Siri.Modules.Catalog.Contracts.ILiveMeetingSink));
            }
            else
            {
                Assert.Equal(typeof(LiveMeetingSink), sink.ImplementationType);
                Assert.Equal(typeof(LiveMeetingReadinessReader), readiness.ImplementationType);
                Assert.Equal(ServiceLifetime.Scoped, sink.Lifetime); // the Singleton Null default is replaced by a Scoped real one
            }
        }
    }

    private static void AddCatalogNullDefaults(IServiceCollection services)
    {
        // Mirrors exactly what CatalogModule does for these two seams.
        Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions
            .TryAddSingleton<Siri.Modules.Catalog.Contracts.ILiveMeetingSink, Siri.Modules.Catalog.Infrastructure.NullLiveMeetingSink>(services);
        Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions
            .TryAddScoped<Siri.Modules.Catalog.Contracts.ILiveMeetingReadinessReader, Siri.Modules.Catalog.Infrastructure.NullLiveMeetingReadinessReader>(services);
    }

    [Fact]
    public void AddLiveModule_LoggingProvider_SelectsTheFakeGoogleServices_OnlyWhenExplicitlyConfigured()
    {
        var fake = new ServiceCollection();
        fake.AddLiveModule(Config(("Seo:PublicBaseUrl", "https://app.example.test"), ("Live:Provider", "Logging"), ("Integrations:Google:RedirectUri", "http://localhost/cb")));
        var real = new ServiceCollection();
        real.AddLiveModule(Config(("Seo:PublicBaseUrl", "https://app.example.test")));

        Assert.Equal(typeof(Siri.Integrations.Google.Logging.LoggingGoogleOAuthService), fake.Last(d => d.ServiceType == typeof(IGoogleOAuthService)).ImplementationType);
        Assert.Equal(typeof(GoogleOAuthService), real.Last(d => d.ServiceType == typeof(IGoogleOAuthService)).ImplementationType);
    }

    [Fact]
    public void AddLiveModule_DefaultsToTheRealProvider_NeverTheFakeOne()
    {
        var services = new ServiceCollection();
        services.AddLiveModule(Config(("Seo:PublicBaseUrl", "https://app.example.test"), ("Live:Provider", "GoogleMeet")));

        Assert.NotEqual(typeof(Siri.Integrations.Google.Logging.LoggingCalendarProvider), services.Last(d => d.ServiceType == typeof(ICalendarProvider)).ImplementationType);
    }

    [Fact]
    public void LiveOptions_FromRegisteredConfiguration_FallBackToSeoPublicBaseUrl()
    {
        var services = new ServiceCollection();
        services.AddLiveModule(Config(("Seo:PublicBaseUrl", "https://seo.example.test")));
        using var provider = services.BuildServiceProvider();

        var options = provider.GetRequiredService<IOptions<LiveOptions>>().Value;

        Assert.Equal("https://seo.example.test", options.PublicBaseUrl);
        Assert.Equal("no-reply@seo.example.test", options.OrganizerEmail);
    }

    [Fact]
    public void LiveOptions_NoPublicBaseUrlAnywhere_FailsValidation()
    {
        var services = new ServiceCollection();
        services.AddLiveModule(Config());
        using var provider = services.BuildServiceProvider();

        Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<LiveOptions>>().Value);
    }

    // ---- Production requirements ---------------------------------------------------------------------------

    [Fact]
    public void Production_LoggingProvider_IsRefused()
    {
        var problems = LiveProductionRequirements.GetProblems(Config(("Seo:PublicBaseUrl", "https://app.example.org"), ("Live:Provider", "Logging")));

        Assert.Contains(problems, p => p.Contains("Live:Provider") && p.Contains("Logging"));
    }

    [Theory]
    [InlineData("logging")]
    [InlineData(" LOGGING ")]
    public void Production_LoggingProvider_IsRefusedWhateverTheCasing(string value)
    {
        var problems = LiveProductionRequirements.GetProblems(Config(("Seo:PublicBaseUrl", "https://app.example.org"), ("Live:Provider", value)));

        Assert.NotEmpty(problems);
    }

    [Fact]
    public void Production_UnknownProvider_IsRefused()
    {
        var problems = LiveProductionRequirements.GetProblems(Config(("Seo:PublicBaseUrl", "https://app.example.org"), ("Live:Provider", "Zoomy")));

        Assert.Contains(problems, p => p.Contains("Zoomy"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("GoogleMeet")]
    [InlineData("ManualOnly")]
    public void Production_RealProviders_AreAccepted_WithGoogleLeftOff(string? provider)
    {
        var problems = LiveProductionRequirements.GetProblems(Config(("Seo:PublicBaseUrl", "https://app.example.org"), ("Live:Provider", provider)));

        Assert.Empty(problems);
    }

    [Fact]
    public void Production_GoogleClientWithoutSecretOrHttpsRedirect_IsRefused()
    {
        var problems = LiveProductionRequirements.GetProblems(Config(
            ("Seo:PublicBaseUrl", "https://app.example.org"),
            ("Integrations:Google:ClientId", "id.apps.googleusercontent.com"),
            ("Integrations:Google:RedirectUri", "http://api.example.org/api/live/instructor/google/callback")));

        Assert.Contains(problems, p => p.Contains("ClientSecret"));
        Assert.Contains(problems, p => p.Contains("RedirectUri"));
    }

    [Fact]
    public void Production_GoogleClientWithPlaceholderSecret_IsRefused()
    {
        var problems = LiveProductionRequirements.GetProblems(Config(
            ("Seo:PublicBaseUrl", "https://app.example.org"),
            ("Integrations:Google:ClientId", "id.apps.googleusercontent.com"),
            ("Integrations:Google:ClientSecret", "CHANGE_ME_please"),
            ("Integrations:Google:RedirectUri", "https://api.example.org/api/live/instructor/google/callback")));

        Assert.Contains(problems, p => p.Contains("ClientSecret"));
        Assert.DoesNotContain(problems, p => p.Contains("RedirectUri"));
    }

    [Fact]
    public void Production_FullyConfiguredGoogle_IsAccepted_AndNoSecretIsEverEchoed()
    {
        var problems = LiveProductionRequirements.GetProblems(Config(
            ("Seo:PublicBaseUrl", "https://app.example.org"),
            ("Integrations:Google:ClientId", "id.apps.googleusercontent.com"),
            ("Integrations:Google:ClientSecret", "real-secret-value"),
            ("Integrations:Google:RedirectUri", "https://api.example.org/api/live/instructor/google/callback")));

        Assert.Empty(problems);
    }

    [Fact]
    public void Production_ProblemsNeverEchoTheSecret()
    {
        var problems = LiveProductionRequirements.GetProblems(Config(
            ("Seo:PublicBaseUrl", "http://insecure.example.org"),
            ("Integrations:Google:ClientId", "id"),
            ("Integrations:Google:ClientSecret", "CHANGE_ME-leaky-secret"),
            ("Integrations:Google:RedirectUri", "http://x")));

        Assert.NotEmpty(problems);
        Assert.DoesNotContain(problems, p => p.Contains("leaky-secret"));
    }

    [Fact]
    public void Production_PublicBaseUrl_MustBeHttps_AndLiveSettingOverridesSeo()
    {
        var insecure = LiveProductionRequirements.GetProblems(Config(("Seo:PublicBaseUrl", "http://app.example.org")));
        var overridden = LiveProductionRequirements.GetProblems(Config(("Seo:PublicBaseUrl", "http://app.example.org"), ("Live:PublicBaseUrl", "https://live.example.org")));
        var missing = LiveProductionRequirements.GetProblems(Config());

        Assert.NotEmpty(insecure);
        Assert.Empty(overridden);
        Assert.NotEmpty(missing);
    }

    [Fact]
    public void ProductionGuard_RefusesToBoot_WithTheLoggingProvider()
    {
        // Minimal otherwise-invalid config: the guard aggregates, so the Live problem must be among the reported ones.
        var configuration = Config(("Live:Provider", "Logging"), ("Seo:PublicBaseUrl", "https://app.example.org"));

        var exception = Assert.Throws<InvalidOperationException>(() =>
            ProductionConfigurationGuard.ValidateProductionConfiguration(configuration, new LiveFakeHostEnvironment("Production")));

        Assert.Contains("Live:Provider", exception.Message);
    }

    [Fact]
    public void ProductionGuard_IgnoresLiveSettings_OutsideProduction()
    {
        var configuration = Config(("Live:Provider", "Logging"));

        ProductionConfigurationGuard.ValidateProductionConfiguration(configuration, new LiveFakeHostEnvironment("Development"));
    }

    private sealed class LiveFakeHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;

        public string ApplicationName { get; set; } = "Siri.Api";

        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }
}
