using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Siri.Integrations.Google;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Live;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Infrastructure;
using Siri.Modules.Notification.Contracts;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;
using StackExchange.Redis;

namespace Siri.UnitTests.Live;

/// <summary>
/// A real container, built with scope validation ON (as in Development), resolving everything the Live module registers. Catches the failures a
/// machine without Docker cannot otherwise see: a missing registration, a captive dependency (a singleton holding a scoped service) or a constructor
/// the container cannot satisfy — each of which would only surface as a 500 (or a dead job) at runtime.
/// </summary>
public class LiveModuleRegistrationTests
{
    private static ServiceProvider BuildProvider(string? liveProvider = null)
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=127.0.0.1;Port=1;Database=unit-test;Username=none;Password=none",
            ["DataProtection:EncryptionKeyBase64"] = Convert.ToBase64String(new byte[32]),
            ["Seo:PublicBaseUrl"] = "https://app.example.test",
            ["Live:Provider"] = liveProvider,
            ["Integrations:Google:RedirectUri"] = "http://localhost/api/live/instructor/google/callback",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence(configuration);
        services.AddLiveModule(configuration);

        // Seams other modules provide (Live reaches them only through Contracts).
        services.AddSingleton<IConnectionMultiplexer>(_ =>
            ConnectionMultiplexer.Connect("127.0.0.1:1,abortConnect=false,connectTimeout=100"));
        services.TryAddScoped<ILiveScheduleReader, StubSchedule>();
        services.TryAddScoped<ICatalogPriceContract, FakeCatalog>();
        services.TryAddScoped<IEmailOutbox, StubOutbox>();
        services.TryAddScoped<IUserContactReader, StubContacts>();
        services.TryAddScoped<IUserNotificationOutbox, RecordingInApp>();
        services.TryAddScoped<Siri.Modules.Learning.Contracts.ILearningAccessContract, FakeLearning>();
        // Catalog's null defaults, exactly as CatalogModule registers them — Live must win over them.
        services.TryAddSingleton<ILiveMeetingSink, Siri.Modules.Catalog.Infrastructure.NullLiveMeetingSink>();
        services.TryAddScoped<ILiveMeetingReadinessReader, Siri.Modules.Catalog.Infrastructure.NullLiveMeetingReadinessReader>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
    }

    [Fact]
    public void EverythingTheModuleRegisters_ResolvesInAScope_WithScopeValidationOn()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        Assert.NotNull(services.GetRequiredService<LiveMeetingSyncJob>());
        Assert.NotNull(services.GetRequiredService<LiveInviteReconcileJob>());
        Assert.NotNull(services.GetRequiredService<LiveSessionRemindersJob>());
        Assert.NotNull(services.GetRequiredService<SessionInviteService>());
        Assert.NotNull(services.GetRequiredService<GoogleAttendeeSyncService>());
        Assert.NotNull(services.GetRequiredService<ISessionInviteRepository>());
        Assert.NotNull(services.GetRequiredService<SessionMeetingService>());
        Assert.NotNull(services.GetRequiredService<InstructorGoogleAccountService>());
        Assert.NotNull(services.GetRequiredService<MeetingLinkValidator>());
        Assert.NotNull(services.GetRequiredService<IInstructorAlertSender>());
        Assert.NotNull(services.GetRequiredService<ISessionMeetingRepository>());
        Assert.NotNull(services.GetRequiredService<IInstructorGoogleAccountRepository>());
        Assert.NotNull(services.GetRequiredService<IGoogleOAuthStateStore>());
        Assert.NotNull(services.GetRequiredService<IGoogleOAuthService>());
        Assert.NotNull(services.GetRequiredService<ICalendarProvider>());
        Assert.NotNull(services.GetRequiredService<ILiveDiagnosticsReader>());
    }

    [Fact]
    public void CatalogSeams_ResolveToLivesImplementations_NotTheNullDefaults()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<LiveMeetingSink>(scope.ServiceProvider.GetRequiredService<ILiveMeetingSink>());
        Assert.IsType<LiveMeetingReadinessReader>(scope.ServiceProvider.GetRequiredService<ILiveMeetingReadinessReader>());
    }

    [Fact]
    public void ValidatorsForTheEndpointsCommands_AreRegistered_SoTheGlobalValidationFilterFindsThem()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        Assert.NotNull(scope.ServiceProvider.GetService<FluentValidation.IValidator<GoogleConnectCommand>>());
        Assert.NotNull(scope.ServiceProvider.GetService<FluentValidation.IValidator<SetMeetingLinkCommand>>());
    }

    [Fact]
    public void DefaultProvider_IsTheRealGoogleImplementation_NeverTheFake()
    {
        using var provider = BuildProvider();

        Assert.IsType<GoogleOAuthService>(provider.GetRequiredService<IGoogleOAuthService>());
        Assert.IsType<GoogleCalendarProvider>(provider.GetRequiredService<ICalendarProvider>());
        Assert.False(provider.GetRequiredService<IGoogleOAuthService>().IsConfigured); // no client id configured => feature off
    }

    [Fact]
    public void LoggingProvider_SelectsTheFakes_AndReportsConfigured()
    {
        using var provider = BuildProvider("Logging");

        Assert.IsType<Siri.Integrations.Google.Logging.LoggingGoogleOAuthService>(provider.GetRequiredService<IGoogleOAuthService>());
        Assert.IsType<Siri.Integrations.Google.Logging.LoggingCalendarProvider>(provider.GetRequiredService<ICalendarProvider>());
        Assert.True(provider.GetRequiredService<IGoogleOAuthService>().IsConfigured);
    }

    private sealed class StubSchedule : ILiveScheduleReader
    {
        public Task<IReadOnlyList<LiveSessionInfo>> GetSessionsForCourseAsync(Guid courseId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<IReadOnlyList<LiveSessionInfo>> GetUpcomingSessionsAsync(DateTime fromUtc, DateTime toUtc, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<LiveSessionInfo?> GetSessionAsync(Guid sessionId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class StubOutbox : IEmailOutbox
    {
        public void Enqueue(string toEmail, string subject, string bodyHtml, string? templateKey) => throw new NotSupportedException();
    }

    private sealed class StubContacts : IUserContactReader
    {
        public Task<string?> GetEmailAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<(string? Email, string? DisplayName)> GetUserContactInfoAsync(Guid userId, CancellationToken cancellationToken) => throw new NotSupportedException();
    }
}
