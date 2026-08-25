using Hangfire;
using Hangfire.Common;
using Siri.Workers;

namespace Siri.UnitTests.Workers;

public sealed class WorkersTests
{
    [Fact]
    public void MapRecurringJobs_RegistersAllScheduledJobs()
    {
        // Arrange
        var mockManager = new FakeRecurringJobManager();

        // Act
        mockManager.MapRecurringJobs();

        // Assert
        Assert.Contains("email-outbox-send", mockManager.RegisteredJobIds);
        Assert.Contains("bunny-transcode-poll", mockManager.RegisteredJobIds);
        Assert.Contains("analytics-nightly-rollup", mockManager.RegisteredJobIds);
        Assert.Contains("stripe-reconciliation", mockManager.RegisteredJobIds);
    }

    private sealed class FakeRecurringJobManager : IRecurringJobManager
    {
        public List<string> RegisteredJobIds { get; } = [];

        public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options)
        {
            RegisteredJobIds.Add(recurringJobId);
        }

        public void Trigger(string recurringJobId) { }

        public void RemoveIfExists(string recurringJobId) { }
    }
}
