using DotNetEnv;
using Hangfire;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Json;
using Siri.Modules.Analytics;
using Siri.Modules.Catalog;
using Siri.Modules.Cms;
using Siri.Modules.Commerce;
using Siri.Modules.Community;
using Siri.Modules.Identity;
using Siri.Modules.Learning;
using Siri.Modules.Media;
using Siri.Modules.Notification;
using Siri.Modules.Payout;
using Siri.Persistence.DependencyInjection;

namespace Siri.Workers;

public static class Program
{
    public static async Task Main(string[] args)
    {
        // Load .env by traversing parent directories from AppContext.BaseDirectory and CurrentDirectory.
        // Gitignored, no-op in production where real env vars are injected by container/host.
        LoadDotEnv();

        // Bootstrap logger: catches early startup errors before host DI is built.
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .Enrich.FromLogContext()
            .WriteTo.Console(new JsonFormatter())
            .CreateBootstrapLogger();

        try
        {
            var builder = Host.CreateApplicationBuilder(args);

            // Serilog configuration
            builder.Services.AddSerilog((services, configuration) =>
            {
                configuration
                    .MinimumLevel.Information()
                    .MinimumLevel.Override("Microsoft", Serilog.Events.LogEventLevel.Warning)
                    .MinimumLevel.Override("Hangfire", Serilog.Events.LogEventLevel.Information)
                    .Enrich.FromLogContext()
                    .Enrich.WithProperty("Application", "Siri.Workers")
                    .WriteTo.Console(new JsonFormatter());

                var otlpEndpoint = builder.Configuration["Observability:OtlpEndpoint"];
                var otlpEnabled = builder.Configuration.GetValue<bool>("Observability:OtlpExportEnabled");

                if (otlpEnabled && !string.IsNullOrWhiteSpace(otlpEndpoint))
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
                            ["service.name"] = builder.Configuration["Observability:ServiceName"] ?? "siri-workers",
                            ["deployment.environment.name"] = builder.Environment.EnvironmentName,
                        };
                    });
                }
            });

            // OpenTelemetry Tracing + Metrics
            var otlpEndpointUrl = builder.Configuration["Observability:OtlpEndpoint"];
            var otlpExportEnabled = builder.Configuration.GetValue<bool>("Observability:OtlpExportEnabled");
            var serviceName = builder.Configuration["Observability:ServiceName"] ?? "siri-workers";

            var openTelemetry = builder.Services.AddOpenTelemetry()
                .ConfigureResource(resource => resource
                    .AddService(serviceName)
                    .AddAttributes([
                        new KeyValuePair<string, object>("deployment.environment.name", builder.Environment.EnvironmentName),
                    ]))
                .WithTracing(tracing => tracing
                    .AddHttpClientInstrumentation())
                .WithMetrics(metrics => metrics
                    .AddHttpClientInstrumentation()
                    .AddRuntimeInstrumentation());

            if (otlpExportEnabled && !string.IsNullOrWhiteSpace(otlpEndpointUrl) && Uri.TryCreate(otlpEndpointUrl, UriKind.Absolute, out var uri))
            {
                var otlpProtocol = builder.Configuration["Observability:OtlpProtocol"];
                openTelemetry.UseOtlpExporter(
                    string.Equals(otlpProtocol, "Grpc", StringComparison.OrdinalIgnoreCase)
                        ? OtlpExportProtocol.Grpc
                        : OtlpExportProtocol.HttpProtobuf,
                    uri);
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

    private static void LoadDotEnv()
    {
        var isProduction = string.Equals(Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(Environment.GetEnvironmentVariable("DOTNET_ENVIRONMENT"), "Production", StringComparison.OrdinalIgnoreCase);

        var candidates = isProduction
            ? new[] { ".env.production", ".env_prd", ".env", ".env.local" }
            : new[] { ".env", ".env.local", ".env.development", ".env_prd", ".env.production" };

        var searchDirs = new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() };
        foreach (var baseDir in searchDirs)
        {
            var dir = new DirectoryInfo(baseDir);
            while (dir != null)
            {
                foreach (var fileName in candidates)
                {
                    var candidate = Path.Combine(dir.FullName, fileName);
                    if (File.Exists(candidate))
                    {
                        Env.Load(candidate, new LoadOptions(clobberExistingVars: true));
                        return;
                    }
                }
                dir = dir.Parent;
            }
        }
    }
}
