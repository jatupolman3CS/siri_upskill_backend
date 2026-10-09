using System.Reflection;
using Hangfire;
using Hangfire.Common;
using Hangfire.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.UnitTests.Live;
using Siri.Workers;

namespace Siri.UnitTests.Workers;

/// <summary>
/// Where the Hangfire processing server runs. Background work only happens while a server is up, and the production image runs only <c>Siri.Api</c>, so the
/// API must be able to host the server and schedule the recurring jobs itself (<c>Hangfire:ServerInApi</c>, default on) — and running it alongside a dedicated
/// Workers deployment must stay harmless.
/// </summary>
public class HangfireHostingTests
{
    private const string Connection = "Host=127.0.0.1;Port=1;Database=unit-test;Username=none;Password=none";

    private static IConfiguration Configuration(string? serverInApi = null)
    {
        var settings = new Dictionary<string, string?> { ["ConnectionStrings:Default"] = Connection };
        if (serverInApi is not null)
        {
            settings[HangfireHostingOptions.ServerInApiKey] = serverInApi;
        }

        return new ConfigurationBuilder().AddInMemoryCollection(settings).Build();
    }

    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "test";

        public string ContentRootPath { get; set; } = ".";

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    // ---- The setting -------------------------------------------------------------------------------------

    [Theory]
    [InlineData("Production")]
    [InlineData("Development")]
    [InlineData("QA")]
    public void ServerInApi_DefaultsToTrue_SoASingleContainerDeploymentIsComplete(string environment) =>
        Assert.True(HangfireHostingOptions.ResolveServerInApi(Configuration(), new Env(environment)));

    [Fact]
    public void ServerInApi_DefaultsToFalse_UnderTheIntegrationTestEnvironment_SoTestHostsNeverSpinUpJobs() =>
        Assert.False(HangfireHostingOptions.ResolveServerInApi(Configuration(), new Env(HangfireHostingOptions.IntegrationTestEnvironmentName)));

    [Fact]
    public void ServerInApi_ExplicitTrue_WinsEvenUnderTheIntegrationTestEnvironment() =>
        Assert.True(HangfireHostingOptions.ResolveServerInApi(Configuration("true"), new Env(HangfireHostingOptions.IntegrationTestEnvironmentName)));

    [Theory]
    [InlineData("false")]
    [InlineData("False")]
    [InlineData(" false ")]
    public void ServerInApi_ExplicitFalse_WinsInProduction(string value) =>
        Assert.False(HangfireHostingOptions.ResolveServerInApi(Configuration(value), new Env("Production")));

    [Fact]
    public void ServerInApi_NotABoolean_FailsFastWithAClearMessage()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => HangfireHostingOptions.ResolveServerInApi(Configuration("maybe"), new Env("Production")));

        Assert.Contains("Hangfire:ServerInApi", ex.Message);
    }

    [Fact]
    public void TheOptionsObject_DefaultsToTrue() => Assert.True(new HangfireHostingOptions().ServerInApi);

    // ---- Registration ------------------------------------------------------------------------------------

    private static (int HostedServices, bool RegistersRecurringJobs, bool Effective) Register(string environment, string? serverInApi)
    {
        var services = new ServiceCollection();
        var configuration = Configuration(serverInApi);
        services.AddHangfireForApi(configuration, new Env(environment));

        var hosted = services.Where(d => d.ServiceType == typeof(IHostedService)).ToArray();
        var registersJobs = hosted.Any(d => d.ImplementationType == typeof(RecurringJobsRegistrationService));
        using var provider = services.BuildServiceProvider();
        var effective = provider.GetRequiredService<IOptions<HangfireHostingOptions>>().Value.ServerInApi;
        return (hosted.Length, registersJobs, effective);
    }

    [Fact]
    public void AddHangfireForApi_ByDefault_RunsTheServerAndSchedulesTheRecurringJobs()
    {
        var (hostedServices, registersJobs, effective) = Register("Production", serverInApi: null);

        Assert.True(effective);
        Assert.True(registersJobs);
        Assert.Equal(2, hostedServices); // Hangfire's processing server + the recurring-job registration
    }

    [Fact]
    public void AddHangfireForApi_WithTheServerSwitchedOff_ReproducesTheOldBehaviour_StorageOnly()
    {
        var (hostedServices, registersJobs, effective) = Register("Production", serverInApi: "false");

        Assert.False(effective);
        Assert.False(registersJobs);
        Assert.Equal(0, hostedServices); // no server, no scheduling: the API only enqueues and serves the dashboard
    }

    [Fact]
    public void AddHangfireForApi_UnderIntegrationTest_StartsNothingUnlessAskedTo()
    {
        Assert.Equal(0, Register(HangfireHostingOptions.IntegrationTestEnvironmentName, serverInApi: null).HostedServices);
        Assert.Equal(2, Register(HangfireHostingOptions.IntegrationTestEnvironmentName, serverInApi: "true").HostedServices);
    }

    [Fact]
    public void AddHangfireForApi_RegistersTheReadOnlyStatusReader_InBothModes()
    {
        foreach (var serverInApi in new[] { "true", "false" })
        {
            var services = new ServiceCollection();
            services.AddHangfireForApi(Configuration(serverInApi), new Env("Production"));

            Assert.Contains(services, d => d.ServiceType == typeof(IBackgroundJobStatusReader));
        }
    }

    [Fact]
    public void AddHangfireForApi_WithoutAConnectionString_FailsAtRegistration()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            new ServiceCollection().AddHangfireForApi(new ConfigurationBuilder().Build(), new Env("Production")));

        Assert.Contains("Missing 'ConnectionStrings:Default'", ex.Message);
    }

    // ---- Recurring-job registration from a host that also runs the server ---------------------------------

    private sealed class FakeRecurringJobManager : IRecurringJobManager
    {
        private readonly object _gate = new();

        public List<(string Id, Job Job, string Cron)> Calls { get; } = [];

        /// <summary>The number of leading <c>AddOrUpdate</c> calls that throw (a database that is not up yet).</summary>
        public int FailFirst { get; set; }

        private int _attempts;

        public int Attempts => _attempts;

        public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options)
        {
            lock (_gate)
            {
                // A failing pass fails on its very first call (nothing recorded yet), so "attempts" counts whole registration passes.
                if (Calls.Count == 0 && _attempts < FailFirst)
                {
                    _attempts++;
                    throw new InvalidOperationException("storage is not reachable yet");
                }

                Calls.Add((recurringJobId, job, cronExpression));
            }
        }

        public void Trigger(string recurringJobId)
        {
        }

        public void RemoveIfExists(string recurringJobId)
        {
        }
    }

    private static async Task<(RecurringJobsRegistrationService Service, ListLogger<RecurringJobsRegistrationService> Log)> RunAsync(FakeRecurringJobManager manager)
    {
        var services = new ServiceCollection().AddSingleton<IRecurringJobManager>(manager).BuildServiceProvider();
        var log = new ListLogger<RecurringJobsRegistrationService>();
        var service = new RecurringJobsRegistrationService(services, log) { RetryDelay = TimeSpan.Zero };

        await service.StartAsync(CancellationToken.None);
        await service.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
        return (service, log);
    }

    [Fact]
    public async Task RegistrationService_SchedulesEveryRecurringJob_UnderTheSharedIds()
    {
        var manager = new FakeRecurringJobManager();

        var (_, log) = await RunAsync(manager);

        Assert.Equal(RecurringJobIds.All.Order().ToArray(), manager.Calls.Select(c => c.Id).Order().ToArray());
        Assert.Contains("registered", log.All, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegistrationService_RetriesWhileTheStorageIsNotReachable_ThenSucceeds_WithoutThrowing()
    {
        var manager = new FakeRecurringJobManager { FailFirst = 3 };

        var (_, log) = await RunAsync(manager);

        Assert.Equal(3, manager.Attempts);
        Assert.Equal(RecurringJobIds.All.Count, manager.Calls.Count);
        Assert.Contains("retrying", log.All, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegistrationService_GivesUpAfterTheLastAttempt_LoggingAnError_AndNeverCrashesTheHost()
    {
        var manager = new FakeRecurringJobManager { FailFirst = int.MaxValue };

        var (service, log) = await RunAsync(manager);

        Assert.True(service.ExecuteTask!.IsCompletedSuccessfully);
        Assert.Equal(RecurringJobsRegistrationService.MaxAttempts, manager.Attempts);
        Assert.Empty(manager.Calls);
        Assert.Contains("Could not register", log.All);
        Assert.DoesNotContain("unit-test", log.All); // never a connection string
    }

    [Fact]
    public async Task RegistrationService_StopsPromptlyWhenTheHostShutsDownBetweenAttempts()
    {
        var manager = new FakeRecurringJobManager { FailFirst = int.MaxValue };
        var services = new ServiceCollection().AddSingleton<IRecurringJobManager>(manager).BuildServiceProvider();
        var service = new RecurringJobsRegistrationService(services, new ListLogger<RecurringJobsRegistrationService>()) { RetryDelay = TimeSpan.FromMinutes(5) };

        await service.StartAsync(CancellationToken.None);
        await service.StopAsync(CancellationToken.None);

        Assert.True(service.ExecuteTask!.IsCompleted);
        Assert.True(manager.Attempts < RecurringJobsRegistrationService.MaxAttempts);
    }

    // ---- Running the API's server AND a dedicated Workers server is harmless ----------------------------------

    private static (List<(string Id, Job Job, string Cron)> Registered, FakeRecurringJobManager Manager) RegisterAll(FakeRecurringJobManager? manager = null)
    {
        manager ??= new FakeRecurringJobManager();
        manager.MapRecurringJobs();
        return (manager.Calls, manager);
    }

    [Fact]
    public void RecurringJobs_RegisteredByTwoHosts_YieldOneDefinitionPerId_NotTwoJobs()
    {
        // Both the API (ServerInApi) and the Workers host call MapRecurringJobs() against the same storage. AddOrUpdate is keyed by id, so the second pass
        // rewrites the identical definition. The fake models storage as a dictionary keyed the way Hangfire keys recurring jobs.
        var apiHost = RegisterAll().Registered;
        var workersHost = RegisterAll().Registered;

        var storage = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var (id, job, cron) in apiHost.Concat(workersHost))
        {
            storage[id] = $"{cron}|{job.Type.FullName}.{job.Method.Name}";
        }

        Assert.Equal(RecurringJobIds.All.Count, storage.Count);
        Assert.Equal(
            apiHost.Select(c => (c.Id, c.Cron, c.Job.Type, c.Job.Method.Name)).OrderBy(c => c.Id).ToArray(),
            workersHost.Select(c => (c.Id, c.Cron, c.Job.Type, c.Job.Method.Name)).OrderBy(c => c.Id).ToArray());
    }

    [Fact]
    public void EveryRegisteredRecurringJob_IsKnownToTheSharedIdList_AndTheOtherWayRound()
    {
        var (registered, _) = RegisterAll();

        var ids = registered.Select(c => c.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct().Count());
        Assert.Equal(RecurringJobIds.All.Order().ToArray(), ids.Order().ToArray());
    }

    /// <summary>
    /// Jobs that are deliberately NOT serialized by a storage lock, each with the reason running two servers (or two overlapping ticks) cannot hurt it.
    /// A new job has to be either locked or added here with its reason — a regression guard on the "two servers are safe" analysis in docs/DEPLOYMENT.md.
    /// </summary>
    private static readonly Dictionary<string, string> UnlockedButSafe = new(StringComparer.Ordinal)
    {
        [RecurringJobIds.CourseEnrollmentRecount] = "recomputes counters from the enrollments themselves, one short row-locked transaction per correction: a second concurrent run is merely redundant",
        [RecurringJobIds.PlaybackAnomalyDetection] = "read-only scan of the last hour that only writes audit rows describing what it saw: an overlapping run repeats an audit line, it corrupts nothing",
        [RecurringJobIds.CourseSearchReindex] = "rewrites every document of the search index from the database (upserts keyed by course id) and deletes documents of unpublished courses: idempotent by construction",
    };

    [Fact]
    public void EveryRecurringJob_IsEitherSerializedByAStorageLock_OrDocumentedAsSafeToOverlap()
    {
        var (registered, _) = RegisterAll();

        foreach (var (id, job, _) in registered)
        {
            var locked = job.Method.GetCustomAttribute<DisableConcurrentExecutionAttribute>() is not null;
            if (locked)
            {
                Assert.False(UnlockedButSafe.ContainsKey(id), $"{id} is locked; remove its entry from the allow-list.");
                continue;
            }

            Assert.True(
                UnlockedButSafe.ContainsKey(id),
                $"Recurring job '{id}' ({job.Type.Name}.{job.Method.Name}) has no [DisableConcurrentExecution] and is not in the documented safe-to-overlap list. " +
                "Two Hangfire servers (API + Workers) share one storage, so every job must either take the storage-backed lock or be idempotent under overlap.");
        }
    }

    [Fact]
    public void TheLockedJobs_UseAStorageBackedLock_SoItHoldsAcrossServers()
    {
        // [DisableConcurrentExecution] is Hangfire's distributed-lock filter: the lock lives in the shared storage (a row in hangfire.lock), not in process
        // memory — which is exactly why it also excludes a run on the other server.
        var attribute = typeof(DisableConcurrentExecutionAttribute);
        Assert.True(typeof(JobFilterAttribute).IsAssignableFrom(attribute));
        Assert.Contains(typeof(IServerFilter), attribute.GetInterfaces());
    }

    [Fact]
    public void TheLiveJobs_AreAllSerialized()
    {
        var (registered, _) = RegisterAll();

        foreach (var id in new[] { RecurringJobIds.LiveMeetingSync, RecurringJobIds.LiveInviteReconcile, RecurringJobIds.LiveSessionReminders, RecurringJobIds.EmailOutboxSend })
        {
            var job = registered.Single(c => c.Id == id).Job;
            Assert.NotNull(job.Method.GetCustomAttribute<DisableConcurrentExecutionAttribute>());
        }
    }

    [Fact]
    public void TheDiagnosticIds_AreASubsetOfTheRegisteredJobs() =>
        Assert.All(RecurringJobIds.LiveDiagnostics, id => Assert.Contains(id, RecurringJobIds.All));
}
