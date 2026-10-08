using System.Reflection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Siri.Modules.Analytics;
using Siri.Modules.Analytics.Features.InstructorAnalytics;
using Siri.Modules.Catalog;
using Siri.Modules.Catalog.Contracts;
using Siri.Modules.Identity.Contracts;
using Siri.Modules.Learning;
using Siri.Modules.Live;
using Siri.Modules.Live.Contracts;
using Siri.Modules.Notification.Contracts;
using Siri.Modules.Payout;
using Siri.Modules.Payout.Application;
using Siri.Modules.Payout.Contracts;
using Siri.Persistence.DependencyInjection;
using StackExchange.Redis;
using Xunit;

namespace Siri.UnitTests.Analytics;

/// <summary>
/// A real container (scope validation ON, like Development) holding the modules the instructor dashboard (P11-10) and the instructor money endpoints pull together: every
/// contract the handler needs has an implementation published by its owning module, and nothing is captured across scopes. A missing registration would otherwise only show
/// up as a 500 on the dashboard of a real instructor.
/// </summary>
public sealed class InstructorDashboardRegistrationTests
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
        services.AddSingleton<IConfiguration>(configuration);
        services.AddPersistence(configuration);
        services.AddCatalogModule(configuration);
        services.AddPayoutModule(configuration);
        services.AddLiveModule(configuration);
        services.AddLearningModule();
        services.AddAnalyticsModule();

        // Seams provided by modules this test does not load (Identity, Notification) and Redis; construction only.
        services.AddSingleton<IConnectionMultiplexer>(_ => ConnectionMultiplexer.Connect("127.0.0.1:1,abortConnect=false,connectTimeout=100"));
        services.TryAddScoped(_ => Unused<IUserContactReader>());
        services.TryAddScoped(_ => Unused<IEmailOutbox>());
        services.TryAddScoped(_ => Unused<IUserNotificationOutbox>());

        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = false });
    }

    [Fact]
    public void TheDashboardHandler_ResolvesInAScope_WithEveryContractItNeedsPublishedByItsOwner()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        Assert.NotNull(services.GetRequiredService<GetInstructorAnalyticsHandler>());
        Assert.IsType<Siri.Modules.Catalog.Infrastructure.Contracts.InstructorCourseStatsReader>(services.GetRequiredService<IInstructorCourseStatsReader>());
        Assert.IsType<Siri.Modules.Payout.Infrastructure.Contracts.InstructorRevenueReader>(services.GetRequiredService<IInstructorRevenueReader>());
        Assert.IsType<Siri.Modules.Live.Infrastructure.LiveAttendanceReader>(services.GetRequiredService<ILiveAttendanceReader>());
        Assert.NotNull(services.GetRequiredService<ILiveScheduleReader>());
    }

    [Fact]
    public void TheInstructorMoneyServices_ResolveWithTheProfileReaderTheyUseToMapUserToProfile()
    {
        using var provider = BuildProvider();
        using var scope = provider.CreateScope();
        var services = scope.ServiceProvider;

        Assert.IsType<Siri.Modules.Catalog.Infrastructure.Contracts.InstructorProfileReader>(services.GetRequiredService<IInstructorProfileReader>());
        Assert.NotNull(services.GetRequiredService<InstructorEarningsService>());
        Assert.NotNull(services.GetRequiredService<InstructorPayoutAccountService>());
        Assert.NotNull(services.GetRequiredService<PayoutBatchService>());
        Assert.NotNull(services.GetRequiredService<RevenueSplitService>());
    }

    [Fact]
    public void TheDashboardHandler_IsScoped_NeverASingleton_BecauseItHoldsRequestScopedDbContexts()
    {
        using var provider = BuildProvider();
        using var first = provider.CreateScope();
        using var second = provider.CreateScope();

        Assert.NotSame(first.ServiceProvider.GetRequiredService<GetInstructorAnalyticsHandler>(), second.ServiceProvider.GetRequiredService<GetInstructorAnalyticsHandler>());
        Assert.Throws<InvalidOperationException>(() => provider.GetRequiredService<GetInstructorAnalyticsHandler>());
    }
}
