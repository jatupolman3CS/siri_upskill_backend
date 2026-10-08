using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning.Contracts;
using Siri.Modules.Live;
using Siri.Modules.Live.Application;
using Siri.Modules.Live.Contracts;
using Siri.Modules.Live.Infrastructure;
using Siri.Modules.Notification.Contracts;
using Siri.Persistence.DependencyInjection;
using StackExchange.Redis;

namespace Siri.UnitTests.Live;

/// <summary>
/// A real container, built with scope validation ON (as in Development), resolving what P11-05 registers: the join gate, the learner/instructor query services, the repositories
/// and the attendance contract. Catches what a machine without Docker cannot otherwise see — a missing registration, a captive dependency or a constructor the container
/// cannot satisfy would surface only as a 500 on the very endpoints that guard the room link.
/// </summary>
public class LiveJoinRegistrationTests
{
    /// <summary>Fails loudly if anything actually calls it: these seams only need to exist for construction.</summary>
    public class UnusedProxy : DispatchProxy
    {
        protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) =>
            throw new NotSupportedException($"{targetMethod?.DeclaringType?.Name}.{targetMethod?.Name} is not expected to be called.");
    }

    private static T Unused<T>()
        where T : class => DispatchProxy.Create<T, UnusedProxy>();

    private static ServiceProvider BuildProvider()
    {
        var settings = new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = "Host=127.0.0.1;Port=1;Database=unit-test;Username=none;Password=none",
            ["DataProtection:EncryptionKeyBase64"] = Convert.ToBase64String(new byte[32]),
            ["Seo:PublicBaseUrl"] = "https://app.example.test",
            ["Integrations:Google:RedirectUri"] = "http://localhost/api/live/instructor/google/callback",
        };
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(settings).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddPersistence(configuration);
        services.AddLiveModule(configuration);

        // Seams other modules provide; Live reaches them only through Contracts.
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("127.0.0.1:1,abortConnect=false,connectTimeout=100"));
        services.TryAddScoped<ILiveScheduleReader>(_ => new JoinFakeSchedule());
        services.TryAddScoped<ICatalogPriceContract>(_ => new FakeCatalog());
        services.TryAddScoped<ICourseSummaryReader>(_ => new FakeCourseSummaries());
        services.TryAddScoped<ILearningAccessContract>(_ => new JoinFakeLearning());
        services.TryAddScoped<IUserContactReader>(_ => new FakeContactReader());
        services.TryAddScoped(_ => Unused<IEmailOutbox>());
        services.TryAddScoped(_ => Unused<IUserNotificationOutbox>());
        services.TryAddSingleton<ILiveMeetingSink, Siri.Modules.Catalog.Infrastructure.NullLiveMeetingSink>();
        services.TryAddScoped<ILiveMeetingReadinessReader, Siri.Modules.Catalog.Infrastructure.NullLiveMeetingReadinessReader>();

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
    }

    [Fact]
    public void TheJoinGateAndTheQueryServices_ResolveInAScope_WithScopeValidationOn()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        Assert.NotNull(services.GetRequiredService<SessionJoinService>());
        Assert.NotNull(services.GetRequiredService<LiveLearnerQueries>());
        Assert.NotNull(services.GetRequiredService<LiveInstructorQueries>());
        Assert.NotNull(services.GetRequiredService<ISessionJoinLogRepository>());
        Assert.NotNull(services.GetRequiredService<ISessionInviteReader>());
    }

    [Fact]
    public void TheAttendanceContract_IsPublishedForOtherModules_AndBacksOntoLivesImplementation()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();

        Assert.IsType<LiveAttendanceReader>(scope.ServiceProvider.GetRequiredService<ILiveAttendanceReader>());
    }

    [Fact]
    public void TheServices_AreScoped_NeverSingletons_BecauseTheyHoldARequestScopedDbContext()
    {
        using var provider = BuildProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Assert.NotSame(first.ServiceProvider.GetRequiredService<SessionJoinService>(), second.ServiceProvider.GetRequiredService<SessionJoinService>());
        Assert.Same(first.ServiceProvider.GetRequiredService<SessionJoinService>(), first.ServiceProvider.GetRequiredService<SessionJoinService>());

        // Resolving from the root with scope validation on must be refused (a singleton would capture the DbContext).
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<SessionJoinService>());
    }
}
