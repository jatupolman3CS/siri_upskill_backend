using Hangfire;
using Hangfire.AspNetCore;
using Microsoft.Extensions.DependencyInjection;

namespace Siri.UnitTests.Workers;

/// <summary>
/// How Hangfire.AspNetCore turns a scheduled job into an object: it asks the container for the class and, when the class itself is not registered,
/// constructs it with <c>ActivatorUtilities</c> from the container's services. So what a scheduled job needs is that <b>its dependencies</b> can be resolved in
/// the host that runs it — which is exactly what <c>HangfireHostingIntegrationTests.EveryScheduledJob_CanBeBuiltFromTheApisOwnContainer</c> checks for the API.
/// Pinned here because that integration test must activate jobs the same way Hangfire does; if a Hangfire upgrade changed the behaviour this test says so.
/// </summary>
public class HangfireJobActivationTests
{
    public sealed class Dependency;

    public sealed class RegisteredJob;

    public sealed class UnregisteredJobWithDependency(Dependency dependency)
    {
        public Dependency Dependency { get; } = dependency;
    }

    private static JobActivatorScope BeginScope(ServiceProvider provider)
    {
        var activator = new AspNetCoreJobActivator(provider.GetRequiredService<IServiceScopeFactory>());

#pragma warning disable CS0618 // the parameterless overload is obsolete but is the only one that needs no storage connection
        return activator.BeginScope();
#pragma warning restore CS0618
    }

    [Fact]
    public void ARegisteredJob_IsResolvedFromTheScope()
    {
        using var provider = new ServiceCollection().AddScoped<RegisteredJob>().BuildServiceProvider();
        using var scope = BeginScope(provider);

        Assert.IsType<RegisteredJob>(scope.Resolve(typeof(RegisteredJob)));
    }

    [Fact]
    public void AnUnregisteredJob_IsConstructedFromTheContainersServices()
    {
        using var provider = new ServiceCollection().AddScoped<Dependency>().BuildServiceProvider();
        using var scope = BeginScope(provider);

        var job = Assert.IsType<UnregisteredJobWithDependency>(scope.Resolve(typeof(UnregisteredJobWithDependency)));
        Assert.NotNull(job.Dependency);
    }

    [Fact]
    public void AJobWhoseDependencyIsMissing_FailsEveryRun()
    {
        using var provider = new ServiceCollection().BuildServiceProvider();
        using var scope = BeginScope(provider);

        Assert.Throws<InvalidOperationException>(() => scope.Resolve(typeof(UnregisteredJobWithDependency)));
    }
}
