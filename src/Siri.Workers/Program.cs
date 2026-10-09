using Siri.SharedKernel.Configuration;
using Hangfire;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Json;
using Siri.Integrations.Email;
using Siri.Integrations.Messaging;
using Siri.Modules.Analytics;
using Siri.Modules.Catalog;
using Siri.Modules.Cms;
using Siri.Modules.Commerce;
using Siri.Modules.Community;
using Siri.Modules.Identity;
using Siri.Modules.Learning;
using Siri.Modules.Live;
using Siri.Modules.Media;
using Siri.Modules.Notification;
using Siri.Modules.Notification.Infrastructure.Delivery;
using Siri.Modules.Payout;
using Siri.Persistence.DependencyInjection;

namespace Siri.Workers;

public static class Program
{
    public static async Task Main(string[] args)
    {
        // See Siri.Api/Program.cs's matching call: surfaces OTLP sink failures (bad network, auth
        // rejected) that Serilog otherwise swallows silently by design. Silent in the normal case.
        Serilog.Debugging.SelfLog.Enable(Console.Error);

        // Bootstrap logger: catches early startup errors before host DI is built.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console(new JsonFormatter())
            .CreateBootstrapLogger();

        try
        {
            var builder = Host.CreateApplicationBuilder(args);
            DotEnvLoader.AddDefaults(builder.Configuration, builder.Environment);

            // This host drains the email outbox, so it must not start in Production while mail would be
            // dropped or fail (Email:Provider unset/'Log'); it also runs the live-meeting-sync job, so it must not
            // start with the fake Live provider or a half-configured Google client; and it decrypts the instructors'
            // stored Google refresh tokens, so it must not start with the placeholder / shipped dev encryption key
            // (its own appsettings.json carries the dev key). Same checks as Siri.Api's ProductionConfigurationGuard —
            // shared so the two can never drift apart (see WorkersProductionRequirements).
            if (builder.Environment.IsProduction())
            {
                var productionProblems = WorkersProductionRequirements.GetProblems(builder.Configuration);
                if (productionProblems.Count > 0)
                {
                    throw new InvalidOperationException(
                        "PRODUCTION CONFIGURATION VALIDATION FAILED:\n - " + string.Join("\n - ", productionProblems));
                }
            }

            // "<project>-<component>-<env>" naming for the shared VPS-wide OTLP collector — same
            // convention/reasoning as Siri.Api/Program.cs's ObservabilityOptions.ServiceName.
            static string EnvironmentSuffix(string environmentName) => environmentName switch
            {
                "Production" => "prd",
                "Development" => "dev",
                "QA" => "qa",
                _ => environmentName.ToLowerInvariant(),
            };
            var workerServiceName =
                $"{builder.Configuration["Observability:ServiceName"] ?? "siriupskill-worker"}-{EnvironmentSuffix(builder.Environment.EnvironmentName)}";

            // See Siri.Api/Program.cs's matching log line — startup signal for "is OTLP actually wired
            // up" without needing to check the collector itself. Never logs OtlpApiKey's value.
            Log.Information(
                "Observability: OTLP export {Status} (endpoint={Endpoint}, protocol={Protocol}, service={ServiceName}, apiKeyConfigured={ApiKeyConfigured})",
                !string.IsNullOrWhiteSpace(builder.Configuration["Observability:OtlpEndpoint"]) ? "enabled" : "disabled",
                builder.Configuration["Observability:OtlpEndpoint"],
                builder.Configuration["Observability:OtlpProtocol"],
                workerServiceName,
                !string.IsNullOrWhiteSpace(builder.Configuration["Observability:OtlpApiKey"]));

            // Serilog configuration
            builder.Services.AddSerilog((services, configuration) =>
            {
                configuration
                    .MinimumLevel.Information()
                    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                    .MinimumLevel.Override("Hangfire", Serilog.Events.LogEventLevel.Information)
                    // See Siri.Api/Program.cs's matching override: quiets the named OTLP exporter
                    // HttpClients' own request/response logs so exporting doesn't create more logs to
                    // export (confirmed via runtime evidence — these clients log at Information, and
                    // that a partial-word "...Otlp" prefix does NOT match; the full names below do).
                    .MinimumLevel.Override("System.Net.Http.HttpClient.OtlpTraceExporter", Serilog.Events.LogEventLevel.Warning)
                    .MinimumLevel.Override("System.Net.Http.HttpClient.OtlpMetricExporter", Serilog.Events.LogEventLevel.Warning)
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("Application", "Siri.Workers")
                    .WriteTo.Console(new JsonFormatter());

                var otlpEndpoint = builder.Configuration["Observability:OtlpEndpoint"];
                // NOTE: previously gated on a literal "Observability:OtlpExportEnabled" config key,
                // which nothing ever sets (OtlpExportEnabled is a computed property on Siri.Api's
                // ObservabilityOptions, not a bound key) — that made this condition permanently false.
                // Fixed to match Siri.Api's Program.cs: enabled <=> endpoint is actually configured.
                var otlpEnabled = !string.IsNullOrWhiteSpace(otlpEndpoint);
                var otlpApiKey = builder.Configuration["Observability:OtlpApiKey"];

                if (otlpEnabled)
                {
                    var otlpProtocol = builder.Configuration["Observability:OtlpProtocol"];
                    configuration.WriteTo.OpenTelemetry(options =>
                    {
                        options.Endpoint = otlpEndpoint;
                        options.Protocol = string.Equals(otlpProtocol, "Grpc", StringComparison.OrdinalIgnoreCase)
                            ? Serilog.Sinks.OpenTelemetry.OtlpProtocol.Grpc
                            : Serilog.Sinks.OpenTelemetry.OtlpProtocol.HttpProtobuf;
                        options.ResourceAttributes = new Dictionary<string, object>
                        {
                            ["service.name"] = workerServiceName,
                            ["deployment.environment.name"] = builder.Environment.EnvironmentName,
                            ["service.instance.id"] = Environment.MachineName,
                        };
                        if (!string.IsNullOrWhiteSpace(otlpApiKey))
                        {
                            options.Headers = new Dictionary<string, string> { ["x-otlp-api-key"] = otlpApiKey };
                        }
                    });
                }
            });

            // OpenTelemetry Tracing + Metrics
            var otlpEndpointUrl = builder.Configuration["Observability:OtlpEndpoint"];
            var otlpExportEnabled = !string.IsNullOrWhiteSpace(otlpEndpointUrl);
            var serviceName = workerServiceName;

            // Same feedback-loop guard as Siri.Api/Program.cs: exclude the exporter's own outbound
            // requests from HttpClient instrumentation, or every exported batch traces itself.
            var workerOtlpHost = otlpExportEnabled && Uri.TryCreate(otlpEndpointUrl, UriKind.Absolute, out var workerOtlpUri)
                ? workerOtlpUri.Host
                : null;
            bool IsNotOtlpExportRequest(HttpRequestMessage request) =>
                workerOtlpHost is null || !string.Equals(request.RequestUri?.Host, workerOtlpHost, StringComparison.OrdinalIgnoreCase);

            var openTelemetry = builder.Services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(serviceName)
                    .AddAttributes([
                        new KeyValuePair<string, object>("deployment.environment.name", builder.Environment.EnvironmentName),
                        new KeyValuePair<string, object>("service.instance.id", Environment.MachineName),
                    ]))
                .WithTracing(tracing => tracing
                    .AddHttpClientInstrumentation(options => options.FilterHttpRequestMessage = IsNotOtlpExportRequest)
                    // Kafka consumption spans (continue the producer's trace via the traceparent header).
                    .AddSource(MessagingTelemetry.SourceName))
                .WithMetrics(metrics => metrics
                    // MeterProviderBuilder.AddHttpClientInstrumentation() (1.17.0) takes no configure
                    // delegate — only the tracing overload supports FilterHttpRequestMessage, so metrics
                    // still aggregate the exporter's own OTLP calls. That's noise, not a feedback loop
                    // (no new export is triggered by recording a metric), so it's left as-is.
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation()
                    // Notification pipeline counters (relay published/failed, email delivered/failed/exhausted/skipped/throttled).
                    .AddMeter(NotificationTelemetry.MeterName));

            if (otlpExportEnabled && Uri.TryCreate(otlpEndpointUrl, UriKind.Absolute, out var uri))
            {
                var otlpProtocol = builder.Configuration["Observability:OtlpProtocol"];
                var otlpApiKeyForSdk = builder.Configuration["Observability:OtlpApiKey"];

                openTelemetry.UseOtlpExporter(
                    string.Equals(otlpProtocol, "Grpc", StringComparison.OrdinalIgnoreCase)
                        ? OtlpExportProtocol.Grpc
                        : OtlpExportProtocol.HttpProtobuf,
                    uri);

                if (!string.IsNullOrWhiteSpace(otlpApiKeyForSdk))
                {
                    // See Siri.Api/Program.cs's matching comment: the OTLP spec's own env var (the
                    // documented way to set headers for UseOtlpExporter) does NOT reach these exporters
                    // in this hosted setup — confirmed by live testing (401s) against the actual VPS
                    // collector. Configuring the named IHttpClientFactory clients directly is what
                    // actually works (verified 200s after this change).
                    builder.Services.AddHttpClient("OtlpTraceExporter",
                        client => client.DefaultRequestHeaders.Add("x-otlp-api-key", otlpApiKeyForSdk));
                    builder.Services.AddHttpClient("OtlpMetricExporter",
                        client => client.DefaultRequestHeaders.Add("x-otlp-api-key", otlpApiKeyForSdk));
                }
            }

            // Module & Infrastructure Services required by Background Jobs
            builder.Services
                .AddPersistence(builder.Configuration)
                .AddSharedRedis(builder.Configuration)
                .AddHangfireWorker(builder.Configuration)
                .AddIdentityModule(builder.Configuration)
                .AddCatalogModule(builder.Configuration)
                .AddMediaModule(builder.Configuration)
                .AddLearningModule()
                .AddCommerceModule(builder.Configuration)
                .AddPayoutModule(builder.Configuration)
                .AddCmsModule()
                .AddCommunityModule()
                .AddNotificationModule(builder.Configuration)
                .AddNotificationDelivery(builder.Configuration)
                .AddLiveModule(builder.Configuration)
                .AddAnalyticsModule();

            var host = builder.Build();

            // Map and schedule recurring platform jobs (email outbox, transcode polling, nightly analytics, PDPA retention, etc.)
            host.Services.GetRequiredService<IRecurringJobManager>().MapRecurringJobs();

            Log.Information("Siri.Workers started successfully. Hangfire processing server is active.");

            await host.RunAsync();
        }
        catch (Exception ex) when (ex is not HostAbortedException)
        {
            Log.Fatal(ex, "Siri.Workers terminated unexpectedly during startup");
            throw;
        }
        finally
        {
            Log.CloseAndFlush();
        }
    }

}
