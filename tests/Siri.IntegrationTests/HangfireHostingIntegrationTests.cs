using System.Net;
using System.Text.Json;
using Hangfire;
using Hangfire.Common;
using Hangfire.Storage;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Siri.IntegrationTests.Fixtures;
using Siri.Modules.Identity.Domain;
using Siri.Persistence;
using Siri.Workers;

namespace Siri.IntegrationTests;

/// <summary>
/// Background work is only done while a Hangfire server runs, and the production image runs only <c>Siri.Api</c>. These prove, against the real composition
/// root, that (1) the API can host the server and schedule the recurring jobs itself when asked (it is the default; the test host switches it off, see
/// <see cref="SiriApiFactory"/>), (2) every job the schedule names can actually be built from the API's own container — so none of them dies on a missing
/// registration the moment it first runs in the API process, and (3) the admin status endpoint reports it truthfully.
/// <para>Requires Docker like every test in this collection (or the local-services recipe in <c>ExternalTestServices</c>).</para>
/// </summary>
[Collection(ContainersCollection.Name)]
public sealed class HangfireHostingIntegrationTests
{
    private readonly ContainersFixture _containers;

    public HangfireHostingIntegrationTests(ContainersFixture containers)
    {
        _containers = containers;
    }

    private sealed class CapturingManager : IRecurringJobManager
    {
        public List<(string Id, Job Job)> Jobs { get; } = [];

        public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options) => Jobs.Add((recurringJobId, job));

        public void Trigger(string recurringJobId)
        {
        }

        public void RemoveIfExists(string recurringJobId)
        {
        }
    }

    private static async Task MigrateAsync(WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<AppDbContext>().Database.MigrateAsync();
    }

    [Fact]
    public async Task EveryScheduledJob_CanBeBuiltFromTheApisOwnContainer()
    {
        await using var factory = new SiriApiFactory(_containers);
        await MigrateAsync(factory);

        var manager = new CapturingManager();
        manager.MapRecurringJobs();
        Assert.Equal(RecurringJobIds.All.Count, manager.Jobs.Count);

        // Hangfire.AspNetCore activates a job by asking the container for its class inside a scope and, when the class itself is not registered, constructing it
        // with ActivatorUtilities from the container's services (pinned by HangfireJobActivationTests). Do exactly that for every scheduled job: a job whose
        // dependencies the API's container cannot supply would fail every run the moment the API's own server executed it. Report all of them at once.
        var unbuildable = new List<string>();
        foreach (var (id, job) in manager.Jobs)
        {
            await using var scope = factory.Services.CreateAsyncScope();
            try
            {
                Assert.NotNull(ActivatorUtilities.GetServiceOrCreateInstance(scope.ServiceProvider, job.Type));
            }
            catch (InvalidOperationException ex)
            {
                unbuildable.Add($"{job.Type.Name} (recurring job '{id}'): {ex.Message}");
            }
        }

        Assert.True(
            unbuildable.Count == 0,
            "These scheduled jobs cannot be built from the API's container, so each run would fail the moment the API's own Hangfire server (or Workers) executed it:"
            + Environment.NewLine + " - " + string.Join(Environment.NewLine + " - ", unbuildable));
    }

    [Fact]
    public async Task WithTheServerSwitchedOnInTheApi_ItRegistersItselfAndSchedulesEveryRecurringJob()
    {
        await using var factory = new SiriApiFactory(_containers, new Dictionary<string, string?> { ["Hangfire:ServerInApi"] = "true" });
        await MigrateAsync(factory);
        using var client = factory.CreateClient();

        var admin = new TestData.TestUserBuilder().WithRole(ROLE.AdminName);
        await using (var scope = factory.Services.CreateAsyncScope())
        {
            await admin.BuildAsync(scope.ServiceProvider);
        }

        var token = await LiveIntegrationSupport.LoginAsync(client, admin);

        // The server announces itself in storage within seconds of the host starting, and the recurring jobs are registered shortly after.
        JsonDocument? status = null;
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            status?.Dispose();
            var (code, body) = await LiveIntegrationSupport.SendAsync(client, LiveIntegrationSupport.Authorized(HttpMethod.Get, "/api/live/admin/status", token));
            Assert.Equal(HttpStatusCode.OK, code);
            status = body;

            if (body.RootElement.GetProperty("jobServer").GetProperty("running").GetBoolean()
                && body.RootElement.GetProperty("recurringJobs").GetArrayLength() == RecurringJobIds.LiveDiagnostics.Count)
            {
                break;
            }

            await Task.Delay(TimeSpan.FromSeconds(1));
        }

        using (status)
        {
            Assert.NotNull(status);
            var root = status.RootElement;
            var jobServer = root.GetProperty("jobServer");

            Assert.True(jobServer.GetProperty("running").GetBoolean(), "No Hangfire server registered within 60 seconds.");
            Assert.True(jobServer.GetProperty("serverCount").GetInt32() >= 1);
            var server = jobServer.GetProperty("servers").EnumerateArray().First();
            Assert.StartsWith("api:", server.GetProperty("name").GetString(), StringComparison.Ordinal);
            Assert.NotEqual(JsonValueKind.Null, server.GetProperty("heartbeatUtc").ValueKind);

            var ids = root.GetProperty("recurringJobs").EnumerateArray().Select(j => j.GetProperty("id").GetString()!).Order().ToArray();
            Assert.Equal(RecurringJobIds.LiveDiagnostics.Order().ToArray(), ids);
            Assert.All(root.GetProperty("recurringJobs").EnumerateArray(), job => Assert.False(string.IsNullOrWhiteSpace(job.GetProperty("cron").GetString())));

            var warnings = root.GetProperty("warnings").EnumerateArray().Select(w => w.GetString()).ToArray();
            Assert.DoesNotContain("no_job_server", warnings);
            Assert.DoesNotContain("recurring_jobs_missing", warnings);
        }
    }

    [Fact]
    public async Task ADedicatedWorkersHostRegisteringTheSameJobsAfterTheApi_LeavesOneDefinitionPerId()
    {
        await using var factory = new SiriApiFactory(_containers, new Dictionary<string, string?> { ["Hangfire:ServerInApi"] = "true" });
        await MigrateAsync(factory);
        _ = factory.Services; // start the host: the API schedules the jobs

        // Wait for the API to have scheduled them, then register again exactly as a Workers host does at its own startup.
        var recurring = factory.Services.GetRequiredService<IRecurringJobManager>();
        var storage = factory.Services.GetRequiredService<JobStorage>();
        await WaitUntilAsync(() => CountRecurring(storage) >= RecurringJobIds.All.Count);

        var before = CountRecurring(storage);
        recurring.MapRecurringJobs();
        recurring.MapRecurringJobs();
        var after = CountRecurring(storage);

        Assert.Equal(RecurringJobIds.All.Count, before);
        Assert.Equal(before, after);
    }

    private static int CountRecurring(JobStorage storage)
    {
        using var connection = storage.GetConnection();
        return connection.GetRecurringJobs().Count(job => RecurringJobIds.All.Contains(job.Id));
    }

    private static async Task WaitUntilAsync(Func<bool> condition)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (!condition() && DateTime.UtcNow < deadline)
        {
            await Task.Delay(TimeSpan.FromMilliseconds(500));
        }
    }
}
