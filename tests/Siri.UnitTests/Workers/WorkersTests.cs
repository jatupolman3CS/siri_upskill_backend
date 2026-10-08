using Hangfire;
using Hangfire.Common;
using Siri.Persistence.DependencyInjection;
using Siri.Workers;

namespace Siri.UnitTests.Workers;

public sealed class WorkersTests
{
    [Fact]
    public void NormalizePostgreSqlConnectionString_ConvertsSqlServerTimeoutAlias()
    {
        const string connectionString = "Server=localhost;Database=SIRIUPSKILL;Password=not-a-secret;Connect Timeout=300";

        var normalized = PersistenceServiceCollectionExtensions.NormalizePostgreSqlConnectionString(connectionString);

        Assert.Contains("Timeout=300", normalized);
        Assert.DoesNotContain("Connect Timeout", normalized, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Password=not-a-secret", normalized);
    }

    [Fact]
    public void NormalizePostgreSqlConnectionString_ConvertsSqlServerHostAndPortSyntax()
    {
        const string connectionString = "Server=127.0.0.1,5432;Database=SIRIUPSKILL;Username=app";

        var normalized = PersistenceServiceCollectionExtensions.NormalizePostgreSqlConnectionString(connectionString);

        Assert.Contains("Host=127.0.0.1;Port=5432", normalized);
        Assert.DoesNotContain("Server=", normalized, StringComparison.OrdinalIgnoreCase);
    }

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

    [Fact]
    public void MapRecurringJobs_RegistersTheHourlyCourseEnrollmentRecount()
    {
        var mockManager = new FakeRecurringJobManager();

        mockManager.MapRecurringJobs();

        // D2: COURSES.ENROLLMENT_COUNT is healed (and filled in for rows that predate its writer) once an hour.
        Assert.Contains("course-enrollment-recount", mockManager.RegisteredJobIds);
        Assert.Equal(Cron.Hourly(), mockManager.CronOf("course-enrollment-recount"));
    }

    [Fact]
    public void AddHangfireClient_WithoutConnectionString_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddHangfireClient(configuration));

        Assert.Contains("Missing 'ConnectionStrings:Default'", ex.Message);
    }

    [Fact]
    public void AddHangfireWorker_WithoutConnectionString_ThrowsInvalidOperationException()
    {
        // Arrange
        var services = new Microsoft.Extensions.DependencyInjection.ServiceCollection();
        var configuration = new Microsoft.Extensions.Configuration.ConfigurationBuilder().Build();

        // Act & Assert
        var ex = Assert.Throws<InvalidOperationException>(() =>
            services.AddHangfireWorker(configuration));

        Assert.Contains("Missing 'ConnectionStrings:Default'", ex.Message);
    }

    private sealed class FakeRecurringJobManager : IRecurringJobManager
    {
        public List<string> RegisteredJobIds { get; } = [];

        private readonly Dictionary<string, string> _crons = [];

        public string CronOf(string recurringJobId) => _crons[recurringJobId];

        public void AddOrUpdate(string recurringJobId, Job job, string cronExpression, RecurringJobOptions options)
        {
            RegisteredJobIds.Add(recurringJobId);
            _crons[recurringJobId] = cronExpression;
        }

        public void Trigger(string recurringJobId) { }

        public void RemoveIfExists(string recurringJobId) { }
    }
}
