using System.Reflection;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Siri.Api.Configuration;
using Siri.Api.Controllers.Live;
using Siri.Api.Diagnostics;
using Siri.Integrations.Email;
using Siri.Integrations.Google;
using Siri.Modules.Live.Application;
using Siri.Modules.Notification.Contracts;
using Siri.SharedKernel;
using Siri.Workers;

namespace Siri.UnitTests.Live;

/// <summary>The warning rules behind <c>GET /api/live/admin/status</c> and the one-line startup log: stable codes, each driven by one fact.</summary>
public class LiveStatusWarningsTests
{
    private static readonly DateTime Now = LiveTestData.Now;

    private static RecurringJobStatusDto Job(string id, DateTime? next = null, string? lastState = "Succeeded") =>
        new(id, "* * * * *", Now.AddSeconds(-20), next ?? Now.AddSeconds(40), lastState);

    private static IReadOnlyList<RecurringJobStatusDto> AllJobs() => RecurringJobIds.LiveDiagnostics.Select(id => Job(id)).ToArray();

    /// <summary>The healthy job list with one job replaced.</summary>
    private static IReadOnlyList<RecurringJobStatusDto> AllJobsWith(RecurringJobStatusDto replacement) =>
        AllJobs().Select(job => job.Id == replacement.Id ? replacement : job).ToArray();

    /// <summary>A fully healthy system, then tweak one thing.</summary>
    private static LiveStatusFacts Healthy(Func<LiveStatusFacts, LiveStatusFacts>? change = null)
    {
        var facts = new LiveStatusFacts(
            Now,
            JobStorageReadable: true,
            JobServerRunning: true,
            AllJobs(),
            RecurringJobIds.LiveDiagnostics,
            EmailProvider: "Smtp",
            EmailStatusReadable: true,
            EmailPendingCount: 0,
            EmailFailedCount: 0,
            EmailOldestPendingAgeSeconds: null,
            LiveProvider: "GoogleMeet",
            GoogleConfigured: true,
            PublicBaseUrl: "https://app.example.test",
            LiveStatusReadable: true,
            MeetingsFailed: 0,
            StuckPendingMeetings: 0);
        return change is null ? facts : change(facts);
    }

    [Fact]
    public void AHealthySystem_HasNoWarnings() => Assert.Empty(LiveStatusWarnings.Evaluate(Healthy()));

    [Fact]
    public void NoJobServer_IsReported_AndIsTheHeadlineWhenNothingElseIsKnown()
    {
        var warnings = LiveStatusWarnings.Evaluate(Healthy(f => f with { JobServerRunning = false }));

        Assert.Contains(LiveStatusWarnings.NoJobServer, warnings);
        Assert.DoesNotContain(LiveStatusWarnings.RecurringJobOverdue, warnings); // overdue only means something while a server is up
    }

    [Fact]
    public void UnreadableStorage_IsReported_WithoutClaimingJobsAreMissing()
    {
        var warnings = LiveStatusWarnings.Evaluate(Healthy(f => f with { JobStorageReadable = false, JobServerRunning = false, RecurringJobs = [] }));

        Assert.Contains(LiveStatusWarnings.JobStorageUnreadable, warnings);
        Assert.Contains(LiveStatusWarnings.NoJobServer, warnings);
        Assert.DoesNotContain(LiveStatusWarnings.RecurringJobsMissing, warnings);
    }

    [Fact]
    public void MissingRecurringJobs_AreReported()
    {
        var warnings = LiveStatusWarnings.Evaluate(Healthy(f => f with { RecurringJobs = AllJobs().Skip(1).ToArray() }));

        Assert.Equal([LiveStatusWarnings.RecurringJobsMissing], warnings);
    }

    [Fact]
    public void AnOverdueRecurringJob_IsReported_OnlyWhenFarPastItsNextRun()
    {
        var justLate = Healthy(f => f with { RecurringJobs = AllJobsWith(Job(RecurringJobIds.LiveMeetingSync, next: Now.AddMinutes(-2))) });
        var overdue = Healthy(f => f with { RecurringJobs = AllJobsWith(Job(RecurringJobIds.LiveMeetingSync, next: Now.AddMinutes(-6))) });

        Assert.Empty(LiveStatusWarnings.Evaluate(justLate));
        Assert.Equal([LiveStatusWarnings.RecurringJobOverdue], LiveStatusWarnings.Evaluate(overdue));
    }

    [Fact]
    public void AFailedLastRun_IsReported()
    {
        var warnings = LiveStatusWarnings.Evaluate(Healthy(f => f with { RecurringJobs = AllJobsWith(Job(RecurringJobIds.EmailOutboxSend, lastState: "Failed")) }));

        Assert.Equal([LiveStatusWarnings.RecurringJobFailing], warnings);
    }

    [Theory]
    [InlineData("Log")]
    [InlineData("Unconfigured")]
    public void AnEmailProviderThatDoesNotDeliver_IsReported(string provider) =>
        Assert.Equal([LiveStatusWarnings.EmailUnconfigured], LiveStatusWarnings.Evaluate(Healthy(f => f with { EmailProvider = provider })));

    [Fact]
    public void TheOutbox_IsABacklog_ByCountOrByAge_ButNotBelowBoth()
    {
        Assert.Empty(LiveStatusWarnings.Evaluate(Healthy(f => f with { EmailPendingCount = 99, EmailOldestPendingAgeSeconds = 599 })));
        Assert.Equal([LiveStatusWarnings.OutboxBacklog], LiveStatusWarnings.Evaluate(Healthy(f => f with { EmailPendingCount = 100 })));
        Assert.Equal([LiveStatusWarnings.OutboxBacklog], LiveStatusWarnings.Evaluate(Healthy(f => f with { EmailPendingCount = 1, EmailOldestPendingAgeSeconds = 601 })));
    }

    [Fact]
    public void AbandonedEmails_AreReported() =>
        Assert.Equal([LiveStatusWarnings.OutboxFailures], LiveStatusWarnings.Evaluate(Healthy(f => f with { EmailFailedCount = 1 })));

    [Fact]
    public void AnUnreadableOutbox_IsReported_AndItsNumbersAreNotJudged() =>
        Assert.Equal(
            [LiveStatusWarnings.EmailStatusUnreadable],
            LiveStatusWarnings.Evaluate(Healthy(f => f with { EmailStatusReadable = false, EmailPendingCount = 9_999, EmailFailedCount = 5 })));

    [Theory]
    [InlineData("http://localhost:4202")]
    [InlineData("")]
    public void ANonHttpsPublicUrl_IsReported(string url) =>
        Assert.Equal([LiveStatusWarnings.PublicBaseUrlNotHttps], LiveStatusWarnings.Evaluate(Healthy(f => f with { PublicBaseUrl = url })));

    [Fact]
    public void GoogleProviderWithoutAClient_IsReported_ButManualOnlyIsNot()
    {
        Assert.Equal([LiveStatusWarnings.GoogleNotConfigured], LiveStatusWarnings.Evaluate(Healthy(f => f with { GoogleConfigured = false })));
        Assert.Empty(LiveStatusWarnings.Evaluate(Healthy(f => f with { LiveProvider = "ManualOnly", GoogleConfigured = false })));
    }

    [Fact]
    public void TheFakeProvider_IsReported() =>
        Assert.Equal([LiveStatusWarnings.LiveProviderLogging], LiveStatusWarnings.Evaluate(Healthy(f => f with { LiveProvider = "Logging" })));

    [Fact]
    public void StuckAndFailedRooms_AreReported()
    {
        Assert.Equal([LiveStatusWarnings.MeetingsStuckPending], LiveStatusWarnings.Evaluate(Healthy(f => f with { StuckPendingMeetings = 1 })));
        Assert.Equal([LiveStatusWarnings.MeetingsFailed], LiveStatusWarnings.Evaluate(Healthy(f => f with { MeetingsFailed = 2 })));
    }

    [Fact]
    public void UnreadableLiveCounters_AreReported_AndNotJudged() =>
        Assert.Equal(
            [LiveStatusWarnings.LiveStatusUnreadable],
            LiveStatusWarnings.Evaluate(Healthy(f => f with { LiveStatusReadable = false, StuckPendingMeetings = 5, MeetingsFailed = 5 })));

    [Fact]
    public void TheExactDeploymentTheOwnerHad_NoWorkers_LogMail_NoGoogle_ReportsEveryProblemAtOnce()
    {
        var warnings = LiveStatusWarnings.Evaluate(Healthy(f => f with
        {
            JobServerRunning = false,
            RecurringJobs = [],
            EmailProvider = "Unconfigured",
            EmailPendingCount = 250,
            GoogleConfigured = false,
            StuckPendingMeetings = 3,
        }));

        Assert.Equal(
            [
                LiveStatusWarnings.NoJobServer,
                LiveStatusWarnings.RecurringJobsMissing,
                LiveStatusWarnings.EmailUnconfigured,
                LiveStatusWarnings.OutboxBacklog,
                LiveStatusWarnings.GoogleNotConfigured,
                LiveStatusWarnings.MeetingsStuckPending,
            ],
            warnings);
    }

    [Fact]
    public void EveryCode_IsStableSnakeCase()
    {
        var codes = typeof(LiveStatusWarnings)
            .GetFields(BindingFlags.Public | BindingFlags.Static)
            .Where(f => f.IsLiteral && f.FieldType == typeof(string))
            .Select(f => (string)f.GetRawConstantValue()!)
            .ToArray();

        Assert.NotEmpty(codes);
        Assert.All(codes, code => Assert.Matches("^[a-z]+(_[a-z]+)*$", code));
        Assert.Equal(codes.Length, codes.Distinct().Count());
        Assert.Contains("no_job_server", codes);
        Assert.Contains("email_unconfigured", codes);
        Assert.Contains("outbox_backlog", codes);
        Assert.Contains("public_base_url_not_https", codes);
        Assert.Contains("google_not_configured", codes);
        Assert.Contains("meetings_stuck_pending", codes);
    }
}

/// <summary>The status service: shape, derived numbers, resilience when one source is down, and "nothing secret in the answer".</summary>
public class LiveAdminStatusServiceTests
{
    private static readonly DateTime Now = LiveTestData.Now;

    private sealed class FakeJobs : IBackgroundJobStatusReader
    {
        public BackgroundJobStatus Status { get; set; } = new([], []);

        public Exception? Throw { get; set; }

        public IReadOnlyCollection<string>? AskedFor { get; private set; }

        public BackgroundJobStatus GetStatus(IReadOnlyCollection<string> recurringJobIds)
        {
            AskedFor = recurringJobIds;
            return Throw is null ? Status : throw Throw;
        }
    }

    private sealed class FakeOutbox : IEmailOutboxHealthReader
    {
        public EmailOutboxHealth Health { get; set; } = new(0, 0, null);

        public Exception? Throw { get; set; }

        public Task<EmailOutboxHealth> GetHealthAsync(CancellationToken cancellationToken) =>
            Throw is null ? Task.FromResult(Health) : Task.FromException<EmailOutboxHealth>(Throw);
    }

    private sealed class FakeCounts : ILiveDiagnosticsReader
    {
        public LiveOperationalCounts Counts { get; set; } = new(0, 0, 0, 0, 0);

        public Exception? Throw { get; set; }

        public Task<LiveOperationalCounts> GetCountsAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
            Throw is null ? Task.FromResult(Counts) : Task.FromException<LiveOperationalCounts>(Throw);
    }

    private readonly FakeJobs _jobs = new();
    private readonly FakeOutbox _outbox = new();
    private readonly FakeCounts _counts = new();
    private bool _googleConfigured = true;
    private string _emailProvider = EmailProviderInfo.Smtp;
    private LiveProviderMode _mode = LiveProviderMode.GoogleMeet;
    private string _publicBaseUrl = "https://courses.example.test";
    private readonly ListLogger<LiveAdminStatusService> _log = new();

    private LiveAdminStatusService Service() =>
        new(
            _jobs,
            _outbox,
            _counts,
            LiveTestData.OptionsOf(o =>
            {
                o.Provider = _mode;
                o.PublicBaseUrl = _publicBaseUrl;
            }),
            new FakeGoogleOAuth { Configured = _googleConfigured },
            new EmailProviderInfo(_emailProvider),
            new FakeClock(Now),
            _log);

    private static BackgroundJobStatus HealthyJobs() =>
        new(
            [new BackgroundServerInfo("api:pod-1:123:abcd", Now.AddHours(-1), Now.AddSeconds(-10))],
            RecurringJobIds.LiveDiagnostics
                .Select(id => new RecurringJobInfo(id, "* * * * *", Now.AddSeconds(-30), Now.AddSeconds(30), "Succeeded"))
                .ToArray());

    [Fact]
    public async Task Healthy_ReportsTheServers_TheFiveJobs_AndNoWarnings()
    {
        _jobs.Status = HealthyJobs();

        var status = await Service().GetStatusAsync(CancellationToken.None);

        Assert.Equal(Now, status.ServerTimeUtc);
        Assert.True(status.JobServer.Running);
        Assert.Equal(1, status.JobServer.ServerCount);
        Assert.Equal("api:pod-1:123:abcd", Assert.Single(status.JobServer.Servers).Name);
        Assert.Equal(RecurringJobIds.LiveDiagnostics.Order().ToArray(), status.RecurringJobs.Select(j => j.Id).Order().ToArray());
        Assert.Equal(RecurringJobIds.LiveDiagnostics, _jobs.AskedFor); // only the Live-related + outbox + recount ids are asked for
        Assert.Empty(status.Warnings);
        Assert.Equal("Smtp", status.Email.Provider);
        Assert.Equal("GoogleMeet", status.Live.Provider);
        Assert.True(status.Live.GoogleConfigured);
        Assert.Equal("courses.example.test", status.Live.PublicBaseUrl);
    }

    [Fact]
    public async Task AServerWhoseHeartbeatIsStale_IsListed_ButIsNotRunning()
    {
        _jobs.Status = new BackgroundJobStatus([new BackgroundServerInfo("dead:1", Now.AddHours(-2), Now.AddMinutes(-4))], HealthyJobs().RecurringJobs);

        var status = await Service().GetStatusAsync(CancellationToken.None);

        Assert.False(status.JobServer.Running);
        Assert.Equal(1, status.JobServer.ServerCount);
        Assert.Contains(LiveStatusWarnings.NoJobServer, status.Warnings);
    }

    [Fact]
    public async Task NoServerAtAll_IsNotRunning_AndWarns()
    {
        var status = await Service().GetStatusAsync(CancellationToken.None);

        Assert.False(status.JobServer.Running);
        Assert.Equal(0, status.JobServer.ServerCount);
        Assert.Contains(LiveStatusWarnings.NoJobServer, status.Warnings);
        Assert.Contains(LiveStatusWarnings.RecurringJobsMissing, status.Warnings);
    }

    [Fact]
    public async Task TheOutboxAge_IsComputedFromTheQueueTime_AndNeverNegative()
    {
        _jobs.Status = HealthyJobs();
        _outbox.Health = new EmailOutboxHealth(3, 1, Now.AddMinutes(-15));

        var status = await Service().GetStatusAsync(CancellationToken.None);

        Assert.Equal(3, status.Email.PendingCount);
        Assert.Equal(1, status.Email.FailedCount);
        Assert.Equal(900, status.Email.OldestPendingAgeSeconds);
        Assert.Contains(LiveStatusWarnings.OutboxBacklog, status.Warnings);
        Assert.Contains(LiveStatusWarnings.OutboxFailures, status.Warnings);

        _outbox.Health = new EmailOutboxHealth(1, 0, Now.AddSeconds(30)); // clock skew: queued "in the future"
        Assert.Equal(0, (await Service().GetStatusAsync(CancellationToken.None)).Email.OldestPendingAgeSeconds);

        _outbox.Health = new EmailOutboxHealth(0, 0, null);
        Assert.Null((await Service().GetStatusAsync(CancellationToken.None)).Email.OldestPendingAgeSeconds);
    }

    [Fact]
    public async Task TheLiveCounters_PassThrough_AndStuckRoomsWarn_WithoutBeingExposedAsANumber()
    {
        _jobs.Status = HealthyJobs();
        _counts.Counts = new LiveOperationalCounts(MeetingsPending: 4, MeetingsAwaitingLink: 7, MeetingsFailed: 2, MeetingsStuckPending: 3, InvitesPending: 9);

        var status = await Service().GetStatusAsync(CancellationToken.None);

        Assert.Equal(4, status.Live.MeetingsPending);
        Assert.Equal(7, status.Live.MeetingsAwaitingLink);
        Assert.Equal(2, status.Live.MeetingsFailed);
        Assert.Equal(9, status.Live.InvitesPending);
        Assert.Contains(LiveStatusWarnings.MeetingsStuckPending, status.Warnings);
        Assert.Contains(LiveStatusWarnings.MeetingsFailed, status.Warnings);
    }

    [Fact]
    public async Task AnUnreadableStorage_StillAnswers_WithAWarning_AndLogsOnlyTheExceptionType()
    {
        _jobs.Throw = new InvalidOperationException("Host=db.internal;Password=hunter2");

        var status = await Service().GetStatusAsync(CancellationToken.None);

        Assert.False(status.JobServer.Running);
        Assert.Contains(LiveStatusWarnings.JobStorageUnreadable, status.Warnings);
        Assert.DoesNotContain("hunter2", _log.All);
        Assert.DoesNotContain("db.internal", _log.All);
        Assert.Contains(nameof(InvalidOperationException), _log.All);
    }

    [Fact]
    public async Task EachSource_FailsIndependently()
    {
        _jobs.Status = HealthyJobs();
        _outbox.Throw = new InvalidOperationException("outbox down");
        _counts.Throw = new InvalidOperationException("live down");

        var status = await Service().GetStatusAsync(CancellationToken.None);

        Assert.True(status.JobServer.Running); // the part that works is still reported
        Assert.Contains(LiveStatusWarnings.EmailStatusUnreadable, status.Warnings);
        Assert.Contains(LiveStatusWarnings.LiveStatusUnreadable, status.Warnings);
        Assert.DoesNotContain(LiveStatusWarnings.NoJobServer, status.Warnings);
    }

    [Fact]
    public async Task Cancellation_IsNotSwallowedAsAnUnreadableSource()
    {
        _outbox.Throw = new OperationCanceledException();
        _jobs.Status = HealthyJobs();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().GetStatusAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("https://courses.example.test/some/path?token=SECRET", "courses.example.test")]
    [InlineData("https://user:pw@courses.example.test:8443/x", "courses.example.test:8443")]
    [InlineData("http://localhost:4202", "localhost:4202")]
    [InlineData("not a url", "")]
    [InlineData("", "")]
    public void ThePublicUrl_IsReducedToItsHostAlone(string configured, string expected) =>
        Assert.Equal(expected, LiveAdminStatusService.HostOf(configured));

    [Fact]
    public async Task TheSerializedAnswer_HasTheContractedShape_CamelCase_AndNoSecrets()
    {
        _publicBaseUrl = "https://user:pw@courses.example.test/secret-path?token=TOPSECRET";
        _jobs.Status = HealthyJobs();
        _outbox.Health = new EmailOutboxHealth(2, 0, Now.AddMinutes(-1));

        var status = await Service().GetStatusAsync(CancellationToken.None);
        var json = JsonSerializer.Serialize(status, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        Assert.Equal(
            new[] { "email", "jobServer", "live", "recurringJobs", "serverTimeUtc", "warnings" },
            root.EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            new[] { "running", "serverCount", "servers" },
            root.GetProperty("jobServer").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            new[] { "heartbeatUtc", "name", "startedAtUtc" },
            root.GetProperty("jobServer").GetProperty("servers")[0].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            new[] { "cron", "id", "lastExecutionUtc", "lastJobState", "nextExecutionUtc" },
            root.GetProperty("recurringJobs")[0].EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            new[] { "failedCount", "oldestPendingAgeSeconds", "pendingCount", "provider" },
            root.GetProperty("email").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());
        Assert.Equal(
            new[] { "googleConfigured", "invitesPending", "meetingsAwaitingLink", "meetingsFailed", "meetingsPending", "provider", "publicBaseUrl" },
            root.GetProperty("live").EnumerateObject().Select(p => p.Name).Order(StringComparer.Ordinal).ToArray());

        Assert.Equal("courses.example.test", root.GetProperty("live").GetProperty("publicBaseUrl").GetString());
        foreach (var secret in new[] { "TOPSECRET", "secret-path", "user:pw", "https://", "@", "token" })
        {
            Assert.DoesNotContain(secret, json, StringComparison.Ordinal);
        }
    }
}

/// <summary>Structural guarantees of the admin status endpoint, so a regression cannot quietly open it up.</summary>
public class LiveAdminControllerContractTests
{
    private static readonly MethodInfo Action = typeof(LiveAdminController).GetMethod(nameof(LiveAdminController.GetStatus))!;

    [Fact]
    public void TheRoute_IsExactlyGetApiLiveAdminStatus()
    {
        var prefix = typeof(LiveAdminController).GetCustomAttribute<RouteAttribute>()!.Template;
        var http = Action.GetCustomAttributes().OfType<HttpMethodAttribute>().Single();

        Assert.Equal("api/live/admin", prefix);
        Assert.Equal("status", http.Template);
        Assert.Equal(["GET"], http.HttpMethods);

        var actions = typeof(LiveAdminController)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly)
            .Where(m => m.GetCustomAttributes().OfType<HttpMethodAttribute>().Any())
            .ToArray();
        Assert.Single(actions); // read-only: one GET, nothing that mutates
    }

    [Fact]
    public void ItIsAdminOnly_WithNoAnonymousAction()
    {
        var authorize = typeof(LiveAdminController).GetCustomAttributes<AuthorizeAttribute>().Concat(Action.GetCustomAttributes<AuthorizeAttribute>()).ToArray();

        Assert.Contains(authorize, a => a.Policy == AuthorizationPolicyNames.AdminOnly);
        Assert.Null(typeof(LiveAdminController).GetCustomAttribute<AllowAnonymousAttribute>());
        Assert.Null(Action.GetCustomAttribute<AllowAnonymousAttribute>());
    }

    [Fact]
    public void ItIsRateLimited_PerUser_NeverWithTheGlobalDefault()
    {
        var policy = (Action.GetCustomAttribute<EnableRateLimitingAttribute>() ?? typeof(LiveAdminController).GetCustomAttribute<EnableRateLimitingAttribute>())?.PolicyName;

        Assert.Equal(RateLimiterConfiguration.LiveUserPolicyName, policy);
        Assert.NotEqual("default", policy);
    }

    [Fact]
    public void ItHasAnEndpointNameSummaryAndResponseTypes()
    {
        Assert.Equal("LiveAdminGetStatus", Action.GetCustomAttribute<EndpointNameAttribute>()!.EndpointName);
        Assert.NotNull(Action.GetCustomAttribute<EndpointSummaryAttribute>());
        Assert.Contains(Action.GetCustomAttributes<ProducesResponseTypeAttribute>(), a => a.StatusCode == StatusCodes.Status200OK && a.Type == typeof(LiveAdminStatusResponse));
        Assert.Contains(Action.GetCustomAttributes<ProducesResponseTypeAttribute>(), a => a.StatusCode == StatusCodes.Status403Forbidden);
    }

    [Fact]
    public void ItTakesNoParametersFromTheRequest() =>
        Assert.All(Action.GetParameters(), p => Assert.True(
            p.GetCustomAttribute<FromServicesAttribute>() is not null || p.ParameterType == typeof(CancellationToken),
            $"{p.Name} must not come from the request."));

    [Fact]
    public async Task TheAnswer_IsNoStore_AndIsTheServicesStatus()
    {
        var service = new LiveAdminStatusService(
            new StatusJobs(),
            new StatusOutbox(),
            new StatusCounts(),
            LiveTestData.OptionsOf(),
            new FakeGoogleOAuth(),
            new EmailProviderInfo(EmailProviderInfo.Smtp),
            new FakeClock(LiveTestData.Now),
            new ListLogger<LiveAdminStatusService>());
        var controller = new LiveAdminController { ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() } };

        var result = await controller.GetStatus(service, CancellationToken.None);

        Assert.Equal("no-store", controller.Response.Headers.CacheControl.ToString());
        var ok = Assert.IsType<Microsoft.AspNetCore.Http.HttpResults.Ok<LiveAdminStatusResponse>>(result);
        Assert.False(ok.Value!.JobServer.Running);
    }

    private sealed class StatusJobs : IBackgroundJobStatusReader
    {
        public BackgroundJobStatus GetStatus(IReadOnlyCollection<string> recurringJobIds) => new([], []);
    }

    private sealed class StatusOutbox : IEmailOutboxHealthReader
    {
        public Task<EmailOutboxHealth> GetHealthAsync(CancellationToken cancellationToken) => Task.FromResult(new EmailOutboxHealth(0, 0, null));
    }

    private sealed class StatusCounts : ILiveDiagnosticsReader
    {
        public Task<LiveOperationalCounts> GetCountsAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
            Task.FromResult(new LiveOperationalCounts(0, 0, 0, 0, 0));
    }
}

/// <summary>The one-shot Production startup line: shown once, only in Production, codes only, and never able to stop the host.</summary>
public class LiveStatusStartupReporterTests
{
    private sealed class Env(string name) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;

        public string ApplicationName { get; set; } = "test";

        public string ContentRootPath { get; set; } = ".";

        public Microsoft.Extensions.FileProviders.IFileProvider ContentRootFileProvider { get; set; } = null!;
    }

    private sealed class RecordingLogger : ILogger<LiveStatusStartupReporter>
    {
        public List<(LogLevel Level, string Message)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
            Entries.Add((logLevel, formatter(state, exception) + (exception is null ? string.Empty : "\n" + exception)));
    }

    private sealed class Jobs(bool throws = false) : IBackgroundJobStatusReader
    {
        public BackgroundJobStatus GetStatus(IReadOnlyCollection<string> recurringJobIds) =>
            throws ? throw new InvalidOperationException("Password=hunter2") : new BackgroundJobStatus([], []);
    }

    private sealed class Outbox : IEmailOutboxHealthReader
    {
        public Task<EmailOutboxHealth> GetHealthAsync(CancellationToken cancellationToken) => Task.FromResult(new EmailOutboxHealth(0, 0, null));
    }

    private sealed class Counts : ILiveDiagnosticsReader
    {
        public Task<LiveOperationalCounts> GetCountsAsync(DateTime nowUtc, CancellationToken cancellationToken) =>
            Task.FromResult(new LiveOperationalCounts(0, 0, 0, 0, 0));
    }

    private static (LiveStatusStartupReporter Reporter, RecordingLogger Log) Build(string environment, bool serverInApi = true, bool healthy = false)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IBackgroundJobStatusReader>(new Jobs());
        services.AddScoped<IEmailOutboxHealthReader, Outbox>();
        services.AddScoped<ILiveDiagnosticsReader, Counts>();
        services.AddSingleton(LiveTestData.OptionsOf(o => o.PublicBaseUrl = healthy ? "https://app.example.test" : "http://localhost:4202"));
        services.AddSingleton<IGoogleOAuthService>(new FakeGoogleOAuth());
        services.AddSingleton(new EmailProviderInfo(healthy ? EmailProviderInfo.Smtp : EmailProviderInfo.Unconfigured));
        services.AddSingleton<IClock>(new FakeClock(LiveTestData.Now));
        services.AddSingleton(typeof(ILogger<>), typeof(Microsoft.Extensions.Logging.Abstractions.NullLogger<>));
        services.AddScoped<LiveAdminStatusService>();
        var provider = services.BuildServiceProvider();

        var log = new RecordingLogger();
        var reporter = new LiveStatusStartupReporter(
            provider.GetRequiredService<IServiceScopeFactory>(),
            new Env(environment),
            Options.Create(new HangfireHostingOptions { ServerInApi = serverInApi }),
            log)
        {
            StartupDelay = TimeSpan.Zero,
        };
        return (reporter, log);
    }

    private static async Task RunAsync(LiveStatusStartupReporter reporter)
    {
        await reporter.StartAsync(CancellationToken.None);
        await reporter.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10));
    }

    [Fact]
    public async Task InProduction_ASingleWarningLine_ListsTheCodes_AndNothingSecret()
    {
        var (reporter, log) = Build("Production");

        await RunAsync(reporter);

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains(LiveStatusWarnings.NoJobServer, entry.Message);
        Assert.Contains(LiveStatusWarnings.EmailUnconfigured, entry.Message);
        Assert.Contains("serverInApi=True", entry.Message);
        Assert.DoesNotContain("localhost", entry.Message); // a code, never the value behind it
    }

    [Fact]
    public async Task OutsideProduction_NothingIsLogged_AndNothingIsChecked()
    {
        foreach (var environment in new[] { "Development", "QA", "IntegrationTest" })
        {
            var (reporter, log) = Build(environment);

            await RunAsync(reporter);

            Assert.Empty(log.Entries);
        }
    }

    [Fact]
    public async Task AnUnexpectedFailureOfTheCheck_IsLoggedByTypeOnly_AndNeverEscapes()
    {
        var services = new ServiceCollection().BuildServiceProvider(); // nothing registered: resolving the status service throws
        var log = new RecordingLogger();
        var reporter = new LiveStatusStartupReporter(
            services.GetRequiredService<IServiceScopeFactory>(),
            new Env("Production"),
            Options.Create(new HangfireHostingOptions()),
            log)
        {
            StartupDelay = TimeSpan.Zero,
        };

        await RunAsync(reporter);

        var entry = Assert.Single(log.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Contains("could not run", entry.Message);
    }

    [Fact]
    public async Task ShuttingDownBeforeTheCheck_EndsQuietly()
    {
        var log = new RecordingLogger();
        var slow = new LiveStatusStartupReporter(
            new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(),
            new Env("Production"),
            Options.Create(new HangfireHostingOptions()),
            log)
        {
            StartupDelay = TimeSpan.FromMinutes(5),
        };

        await slow.StartAsync(CancellationToken.None);
        await slow.StopAsync(CancellationToken.None);

        Assert.True(slow.ExecuteTask!.IsCompleted);
        Assert.Empty(log.Entries);
    }
}
