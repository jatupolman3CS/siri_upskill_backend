using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Siri.Integrations.Google;
using Siri.Integrations.Google.Logging;

namespace Siri.UnitTests.Google;

public sealed class GoogleOAuthOptionsTests
{
    private static ValidateOptionsResult Validate(GoogleOAuthOptions options) =>
        new GoogleOAuthOptionsValidator().Validate(null, options);

    private static GoogleOAuthOptions ValidConfigured() => new()
    {
        ClientId = "client-id.apps.googleusercontent.com",
        ClientSecret = "client-secret",
        RedirectUri = "https://api.example.com/api/live/instructor/google/callback",
    };

    [Fact]
    public void Validate_EmptyClientId_PassesAndMeansFeatureOff()
    {
        var options = new GoogleOAuthOptions();

        Assert.True(Validate(options).Succeeded);
        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void Validate_FullyConfigured_Passes()
    {
        Assert.True(Validate(ValidConfigured()).Succeeded);
    }

    [Fact]
    public void Validate_ClientIdWithoutSecret_Fails()
    {
        var options = ValidConfigured();
        options.ClientSecret = " ";

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("ClientSecret"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not a url")]
    [InlineData("/relative/callback")]
    [InlineData("http://api.example.com/callback")] // http only allowed for loopback
    [InlineData("ftp://api.example.com/callback")]
    public void Validate_ClientIdWithBadRedirectUri_Fails(string redirectUri)
    {
        var options = ValidConfigured();
        options.RedirectUri = redirectUri;

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("RedirectUri"));
    }

    [Theory]
    [InlineData("http://localhost:5190/api/live/instructor/google/callback")]
    [InlineData("http://127.0.0.1:5190/cb")]
    [InlineData("https://api.example.com/cb")]
    public void Validate_ClientIdWithHttpsOrLoopbackRedirectUri_Passes(string redirectUri)
    {
        var options = ValidConfigured();
        options.RedirectUri = redirectUri;

        Assert.True(Validate(options).Succeeded);
    }

    [Theory]
    [InlineData("email", GoogleScopes.CalendarEventsOwned)] // missing openid
    [InlineData("openid", GoogleScopes.CalendarEventsOwned)] // missing email
    [InlineData("openid", "email")] // no calendar scope
    public void Validate_ScopesMissingARequiredScope_Fails(params string[] scopes)
    {
        var options = ValidConfigured();
        options.Scopes = scopes;

        var result = Validate(options);

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("Scopes"));
    }

    [Theory]
    [InlineData(GoogleScopes.CalendarEventsOwned)]
    [InlineData(GoogleScopes.CalendarEvents)]
    [InlineData(GoogleScopes.Calendar)]
    public void Validate_AnyAcceptedCalendarScope_Passes(string calendarScope)
    {
        var options = ValidConfigured();
        options.Scopes = ["openid", "email", calendarScope];

        Assert.True(Validate(options).Succeeded);
    }

    [Fact]
    public void Validate_ReadOnlyCalendarScope_IsNotEnough()
    {
        var options = ValidConfigured();
        options.Scopes = ["openid", "email", "https://www.googleapis.com/auth/calendar.readonly"];

        Assert.True(Validate(options).Failed);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(121)]
    [InlineData(-5)]
    public void Validate_HttpTimeoutOutOfRange_FailsEvenWhenFeatureIsOff(int seconds)
    {
        var result = Validate(new GoogleOAuthOptions { HttpTimeoutSeconds = seconds });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("HttpTimeoutSeconds"));
    }

    [Theory]
    [InlineData("/instructor/live-settings", true)]
    [InlineData("/instructor/courses/123/edit", true)]
    [InlineData("/instructor/", true)]
    [InlineData("", false)]
    [InlineData("/admin/users", false)]
    [InlineData("/learn/instructor/x", false)]
    [InlineData("https://evil.example/instructor/x", false)]
    [InlineData("//evil.example/instructor/x", false)]
    [InlineData("/instructor//evil", false)]
    [InlineData("/instructor/../admin", false)]
    [InlineData("/instructor/a\\b", false)]
    [InlineData("/instructor/a?next=https://evil", false)]
    [InlineData("/instructor/a b", false)]
    [InlineData("/instructor/a\n", false)] // '$' would accept a trailing newline; the pattern is anchored with \z
    public void IsSafeInstructorPath_AllowsOnlyInstructorAreaPaths(string path, bool expected)
    {
        Assert.Equal(expected, GoogleOAuthOptions.IsSafeInstructorPath(path));
    }

    [Fact]
    public void IsSafeInstructorPath_RejectsOver200Characters()
    {
        Assert.True(GoogleOAuthOptions.IsSafeInstructorPath("/instructor/" + new string('a', 188)));
        Assert.False(GoogleOAuthOptions.IsSafeInstructorPath("/instructor/" + new string('a', 189)));
    }

    [Theory]
    [InlineData("/not-instructor")]
    [InlineData("https://evil.example/instructor/x")]
    public void Validate_PostConnectRedirectPathOutsideInstructorArea_Fails(string path)
    {
        var result = Validate(new GoogleOAuthOptions { PostConnectRedirectPath = path });

        Assert.True(result.Failed);
        Assert.Contains(result.Failures!, f => f.Contains("PostConnectRedirectPath"));
    }

    [Fact]
    public void GetEffectiveScopes_WhenNoneConfigured_FallsBackToTheContractDefault()
    {
        Assert.Equal(
            ["openid", "email", "https://www.googleapis.com/auth/calendar.events.owned"],
            new GoogleOAuthOptions().GetEffectiveScopes());
    }

    [Fact]
    public void GetEffectiveScopes_TrimsDeDuplicatesAndKeepsOrder()
    {
        var options = new GoogleOAuthOptions { Scopes = [" openid ", "email", "", "openid", GoogleScopes.CalendarEvents] };

        Assert.Equal(["openid", "email", GoogleScopes.CalendarEvents], options.GetEffectiveScopes());
    }

    [Fact]
    public void Binding_ScopesFromConfiguration_ReplaceDefaultsWithoutDuplicating()
    {
        // The config binder APPENDS to a pre-populated array; Scopes defaults to empty so appsettings can carry the
        // very same three scopes the contract lists without them being doubled.
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Integrations:Google:ClientId"] = "id",
                ["Integrations:Google:ClientSecret"] = "secret",
                ["Integrations:Google:RedirectUri"] = "https://api.example.com/cb",
                ["Integrations:Google:Scopes:0"] = "openid",
                ["Integrations:Google:Scopes:1"] = "email",
                ["Integrations:Google:Scopes:2"] = GoogleScopes.CalendarEventsOwned,
            })
            .Build();

        var options = configuration.GetSection(GoogleOAuthOptions.SectionName).Get<GoogleOAuthOptions>()!;

        Assert.Equal(["openid", "email", GoogleScopes.CalendarEventsOwned], options.Scopes);
        Assert.True(options.IsConfigured);
    }

    [Theory]
    [InlineData("openid email https://www.googleapis.com/auth/calendar.events.owned", true)]
    [InlineData("openid https://www.googleapis.com/auth/calendar.events", true)]
    [InlineData("https://www.googleapis.com/auth/calendar", true)]
    [InlineData("openid email", false)]
    [InlineData("https://www.googleapis.com/auth/calendar.readonly", false)]
    [InlineData("https://www.googleapis.com/auth/calendar.events.owned.readonly", false)]
    [InlineData("https://www.googleapis.com/auth/calendar.app.created", false)]
    [InlineData("https://evil.example/https://www.googleapis.com/auth/calendar", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void HasCalendarScope_MatchesExactScopeTokensOnly(string? granted, bool expected)
    {
        Assert.Equal(expected, GoogleScopes.HasCalendarScope(granted));
    }

    // ---- DI registration (AddGoogleIntegration) ----------------------------------------------------------------

    private static ServiceProvider Build(IDictionary<string, string?> settings, bool useLogging = false)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddGoogleIntegration(configuration, useLogging);
        return services.BuildServiceProvider();
    }

    [Fact]
    public void AddGoogleIntegration_NoConfiguration_RegistersRealServicesThatReportNotConfigured()
    {
        using var provider = Build(new Dictionary<string, string?>());

        Assert.IsType<GoogleOAuthService>(provider.GetRequiredService<IGoogleOAuthService>());
        Assert.IsType<GoogleCalendarProvider>(provider.GetRequiredService<ICalendarProvider>());
        Assert.False(provider.GetRequiredService<IGoogleOAuthService>().IsConfigured);
    }

    [Fact]
    public void AddGoogleIntegration_UseLoggingTrue_RegistersTheDevFakes()
    {
        using var provider = Build(
            new Dictionary<string, string?> { ["Integrations:Google:RedirectUri"] = "http://localhost:5190/api/live/instructor/google/callback" },
            useLogging: true);

        Assert.IsType<LoggingGoogleOAuthService>(provider.GetRequiredService<IGoogleOAuthService>());
        Assert.IsType<LoggingCalendarProvider>(provider.GetRequiredService<ICalendarProvider>());
        Assert.True(provider.GetRequiredService<IGoogleOAuthService>().IsConfigured);
    }

    [Fact]
    public void AddGoogleIntegration_LoggingWithoutRedirectUri_FailsOptionsValidation()
    {
        using var provider = Build(new Dictionary<string, string?>(), useLogging: true);

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<GoogleOAuthOptions>>().Value);

        Assert.Contains("RedirectUri", ex.Message);
    }

    [Fact]
    public void AddGoogleIntegration_HalfConfiguredClient_FailsOptionsValidation()
    {
        using var provider = Build(new Dictionary<string, string?> { ["Integrations:Google:ClientId"] = "id-without-secret" });

        var ex = Assert.Throws<OptionsValidationException>(() => provider.GetRequiredService<IOptions<GoogleOAuthOptions>>().Value);

        Assert.Contains("ClientSecret", ex.Message);
    }

    [Fact]
    public void AddGoogleIntegration_NamedClients_UseTheConfiguredTimeout()
    {
        using var provider = Build(new Dictionary<string, string?> { ["Integrations:Google:HttpTimeoutSeconds"] = "7" });
        var factory = provider.GetRequiredService<IHttpClientFactory>();

        Assert.Equal(TimeSpan.FromSeconds(7), factory.CreateClient(GoogleOAuthService.HttpClientName).Timeout);
        Assert.Equal(TimeSpan.FromSeconds(7), factory.CreateClient(GoogleCalendarProvider.HttpClientName).Timeout);
    }

    [Fact]
    public void AddGoogleIntegration_DefaultTimeoutIs15Seconds()
    {
        using var provider = Build(new Dictionary<string, string?>());

        Assert.Equal(TimeSpan.FromSeconds(15), provider.GetRequiredService<IHttpClientFactory>().CreateClient(GoogleOAuthService.HttpClientName).Timeout);
    }
}
