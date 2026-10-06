using System.Security.Claims;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Serilog;
using Serilog.Formatting.Json;
using Siri.Api.Authorization;
using Siri.Api.Configuration;
using Siri.Api.ErrorHandling;
using Siri.Api.Middleware;
using Siri.Api.Observability;
using Siri.Modules.Analytics;
using Siri.Modules.Catalog;
using Siri.Modules.Catalog.Infrastructure.Seeding;
using Siri.Modules.Cms;
using Siri.Modules.Commerce;
using Siri.Modules.Community;
using Siri.Modules.Identity;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Identity.Infrastructure.Seeding;
using Siri.Modules.Learning;
using Siri.Modules.Media;
using Siri.Modules.Media.Infrastructure.Seeding;
using Siri.Modules.Notification;
using Siri.Modules.Payout;
using Siri.Persistence.DependencyInjection;
using Siri.SharedKernel;
using Siri.SharedKernel.Configuration;
using Siri.Workers;

// Serilog swallows sink failures (bad network, malformed export, OTLP auth rejected) by design so
// they never disrupt app logging — SelfLog is the documented escape hatch to see them. Kept on
// permanently (writes to stderr only on an actual sink failure, so it's silent in the normal case)
// since P0-13's OTLP wiring has no other way to surface a misconfigured/unreachable collector.
Serilog.Debugging.SelfLog.Enable(Console.Error);

// Bootstrap logger: catches anything that goes wrong before the host's own Serilog pipeline
// (built further down from configuration) is ready.
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console(new JsonFormatter())
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);
    DotEnvLoader.AddDefaults(builder.Configuration, builder.Environment);

    // P0-13 observability: read the section directly here (same reasoning as JwtOptions below —
    // Serilog's pipeline and AddOpenTelemetry's exporter registration both happen before the DI
    // container that would resolve IOptions<ObservabilityOptions> exists). The options type is
    // still registered with ValidateOnStart() further down so a malformed endpoint fails the boot
    // loudly instead of silently exporting nowhere.
    var observability = builder.Configuration.GetSection(ObservabilityOptions.SectionName)
        .Get<ObservabilityOptions>() ?? new ObservabilityOptions();
    // "<project>-<component>-<env>" naming for the shared VPS-wide OTLP collector (see
    // ObservabilityOptions.ServiceName's doc comment) — applied unconditionally so the same rule
    // holds whether ServiceName was left at its default or set explicitly via config.
    observability.ServiceName = $"{observability.ServiceName}-{EnvironmentSuffix(builder.Environment.EnvironmentName)}";

    static string EnvironmentSuffix(string environmentName) => environmentName switch
    {
        "Production" => "prd",
        "Development" => "dev",
        "QA" => "qa",
        _ => environmentName.ToLowerInvariant(),
    };

    // Startup signal for "is OTLP actually wired up" without needing to check the collector itself —
    // never logs OtlpApiKey's value (security.md), only whether one is set.
    Log.Information(
        "Observability: OTLP export {Status} (endpoint={Endpoint}, protocol={Protocol}, service={ServiceName}, apiKeyConfigured={ApiKeyConfigured})",
        observability.OtlpExportEnabled ? "enabled" : "disabled",
        observability.OtlpExportEnabled ? observability.OtlpEndpoint : null,
        observability.OtlpProtocol,
        observability.ServiceName,
        !string.IsNullOrWhiteSpace(observability.OtlpApiKey));

    builder.Host.UseSerilog((context, services, configuration) =>
    {
        configuration
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
            // The OTLP SDK exporters (traces/metrics) run through named IHttpClientFactory clients
            // "OtlpTraceExporter"/"OtlpMetricExporter" (confirmed from their own request/response log
            // lines at runtime — System.Net.Http.HttpClient.Otlp*Exporter.{LogicalHandler,ClientHandler}),
            // which by default log every export call at Information. Left alone, that's the exact
            // feedback loop this wiring must avoid: exporting a log batch about exporting a log batch.
            // Full source context per client, not a partial-word prefix — confirmed empirically that
            // Serilog's Override match did NOT fire for "...Otlp" alone (these two exact strings do).
            // Every other named HttpClient (Stripe, Bunny, ...) keeps logging normally.
            .MinimumLevel.Override("System.Net.Http.HttpClient.OtlpTraceExporter", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("System.Net.Http.HttpClient.OtlpMetricExporter", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "Siri.Api")
            .WriteTo.Console(new JsonFormatter());

        if (observability.OtlpExportEnabled)
        {
            // Log signal of ARCHITECTURE.md §5's "trace/metric/log → OTLP". Base endpoint only —
            // the sink appends /v1/logs itself for HttpProtobuf (its Endpoint doc explicitly says
            // standard OTLP paths "should not be specified, and will be trimmed if present").
            configuration.WriteTo.OpenTelemetry(options =>
            {
                options.Endpoint = observability.OtlpEndpoint;
                options.Protocol = observability.OtlpProtocol == OtlpTransport.Grpc
                    ? Serilog.Sinks.OpenTelemetry.OtlpProtocol.Grpc
                    : Serilog.Sinks.OpenTelemetry.OtlpProtocol.HttpProtobuf;
                // The sink builds its own OTLP resource (it doesn't share the OpenTelemetry SDK's
                // ResourceBuilder below), so service.name must be set here too or logs would arrive
                // under the sink's default "unknown_service" and not correlate with traces/metrics.
                options.ResourceAttributes = new Dictionary<string, object>
                {
                    ["service.name"] = observability.ServiceName,
                    ["deployment.environment.name"] = context.HostingEnvironment.EnvironmentName,
                    ["service.instance.id"] = Environment.MachineName,
                };
                if (!string.IsNullOrWhiteSpace(observability.OtlpApiKey))
                {
                    options.Headers = new Dictionary<string, string> { ["x-otlp-api-key"] = observability.OtlpApiKey };
                }
            });
        }
    });

    // ---- Services -----------------------------------------------------------------------

    // P1-03: InstructorApplicationStatus is this codebase's first enum exposed through an API response.
    // Configured globally (not per-DTO) so every future module's enum (CourseStatus, CourseLevel, ...)
    // serializes as its readable name by default instead of a bare int — matches database.md's own
    // HasConversion<string>() convention for how enums are stored, and reads better in the OpenAPI
    // schema AddOpenApi() below generates from these same JsonSerializerOptions. Zero-risk: no enum has
    // been API-exposed before this task, so nothing depends on the previous (numeric) default.
    builder.Services.ConfigureHttpJsonOptions(options =>
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));

    builder.Services.AddOpenApi();

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("Default", policy =>
        {
            if (builder.Environment.IsDevelopment())
            {
                policy
                    .SetIsOriginAllowed(_ => true)
                    .AllowAnyHeader()
                    .AllowAnyMethod()
                    .AllowCredentials();
            }
            else
            {
                var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

                if (allowedOrigins.Length > 0)
                {
                    policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
                }
                else
                {
                    policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
                }
            }
        });
    });

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        // "auth": applied via .RequireRateLimiting("auth") on Identity's Register/ConfirmEmail (P0-15)
        // and now Login/Refresh (P0-16) too. Still global/unpartitioned (not per-IP/per-key), so it
        // throttles each endpoint as a whole rather than each caller individually — partitioning is a
        // separate, cross-cutting change since it would affect every endpoint already on this policy.
        options.AddFixedWindowLimiter("auth", limiter =>
        {
            limiter.Window = TimeSpan.FromMinutes(1);
            // Local navigation and reloads share this limiter with login and token refresh.
            limiter.PermitLimit = builder.Environment.IsDevelopment() ? 100 : 5;
            limiter.QueueLimit = 0;
        });

        // "default": not yet applied to any endpoint — reserved for non-auth public endpoints
        // (browse/catalog) once they need one, per ARCHITECTURE.md §2 "Rate limit".
        options.AddFixedWindowLimiter("default", limiter =>
        {
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.PermitLimit = 100;
            limiter.QueueLimit = 0;
        });

        // "webhook": applied via .RequireRateLimiting("webhook") on payment webhook endpoints (P3-04).
        options.AddFixedWindowLimiter("webhook", limiter =>
        {
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.PermitLimit = 120;
            limiter.QueueLimit = 0;
        });

        // "heartbeat": applied to playback progress heartbeat endpoints (X-29). Partitioned per user
        // so that concurrent learners never exhaust a global quota. 6 requests per 30 seconds allows
        // the normal 15s interval (2 requests/30s) plus bursts from seek/resume events.
        options.AddPolicy<string>(
            RateLimiterConfiguration.HeartbeatPolicyName,
            RateLimiterConfiguration.CreateHeartbeatPartition);
    });

    builder.Services.AddHealthChecks();

    // ---- OpenTelemetry traces + metrics (P0-13) ------------------------------------------
    // Instrumentation is always on (near-zero cost with no exporter attached — see
    // ObservabilityOptions' doc comment); the OTLP exporter itself is config-gated so dev/CI run
    // exporter-less while the VPS points Observability:OtlpEndpoint at its collector.
    // DB-level spans (EF Core / Npgsql instrumentation) are deliberately absent: both contrib
    // packages are still beta-only on NuGet (checked 2026-08-18, latest is 1.17.0-beta.1) and this
    // codebase doesn't take prerelease dependencies — revisit when either goes stable.
    builder.Services.AddOptions<ObservabilityOptions>()
        .Bind(builder.Configuration.GetSection(ObservabilityOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    // The OTLP exporter's own outbound calls go through HttpClient like any other, which
    // AddHttpClientInstrumentation() below would otherwise trace/measure like any other outgoing
    // call — every exported batch would generate a new span for exporting itself, which gets
    // exported too: an unbounded feedback loop, not just noise. Filtering by host (computed once,
    // not the full endpoint with path) keeps this correct even though the SDK appends the
    // per-signal path (/v1/traces, /v1/metrics) to OtlpEndpoint itself for HttpProtobuf.
    var otlpHost = observability.OtlpExportEnabled ? observability.GetOtlpEndpointUri().Host : null;
    bool IsNotOtlpExportRequest(HttpRequestMessage request) =>
        otlpHost is null || !string.Equals(request.RequestUri?.Host, otlpHost, StringComparison.OrdinalIgnoreCase);

    var openTelemetry = builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource
            .AddService(observability.ServiceName)
            .AddAttributes([
                new KeyValuePair<string, object>(
                    "deployment.environment.name", builder.Environment.EnvironmentName),
                new KeyValuePair<string, object>("service.instance.id", Environment.MachineName),
            ]))
        .WithTracing(tracing => tracing
            // /health is polled continuously by Docker/Caddy/Uptime Kuma (DEPLOYMENT.md) — tracing
            // every probe would drown real request spans in noise.
            .AddAspNetCoreInstrumentation(options =>
                options.Filter = httpContext => httpContext.Request.Path != "/health")
            .AddHttpClientInstrumentation(options => options.FilterHttpRequestMessage = IsNotOtlpExportRequest))
        .WithMetrics(metrics => metrics
            .AddAspNetCoreInstrumentation()
            // MeterProviderBuilder.AddHttpClientInstrumentation() (1.17.0) takes no configure delegate —
            // only the tracing overload supports FilterHttpRequestMessage, so metrics still aggregate the
            // exporter's own OTLP calls. That's noise, not a feedback loop (recording a metric doesn't
            // trigger a new export), so it's left as-is.
            .AddHttpClientInstrumentation()
            .AddRuntimeInstrumentation());

    if (observability.OtlpExportEnabled)
    {
        openTelemetry.UseOtlpExporter(
            observability.OtlpProtocol == OtlpTransport.Grpc
                ? OtlpExportProtocol.Grpc
                : OtlpExportProtocol.HttpProtobuf,
            observability.GetOtlpEndpointUri());

        if (!string.IsNullOrWhiteSpace(observability.OtlpApiKey))
        {
            // UseOtlpExporter(protocol, uri) has no headers parameter, and setting the OTLP spec's own
            // OTEL_EXPORTER_OTLP_HEADERS env var (the documented approach for headers) does NOT reach
            // these exporters in this hosted setup — confirmed by live testing against the actual VPS
            // collector: the trace/metric exporters run through named IHttpClientFactory clients
            // "OtlpTraceExporter"/"OtlpMetricExporter" (see their own request logs, now quieted above),
            // and those got 401s with the env var approach. Configuring the named clients' default
            // headers directly is what actually works — verified the same way (200s after this change).
            builder.Services.AddHttpClient("OtlpTraceExporter",
                client => client.DefaultRequestHeaders.Add("x-otlp-api-key", observability.OtlpApiKey));
            builder.Services.AddHttpClient("OtlpMetricExporter",
                client => client.DefaultRequestHeaders.Add("x-otlp-api-key", observability.OtlpApiKey));
        }
    }

    // ---- Authentication / Authorization (P0-16) ------------------------------------------
    // JwtBearer validates the access tokens Siri.Modules.Identity's AccessTokenGenerator issues
    // (Login/Refresh). Read directly off IConfiguration here (not IOptions<JwtOptions>, which
    // IdentityModule.AddIdentityModule also registers off this exact same "Identity:Jwt" section) —
    // AddJwtBearer's TokenValidationParameters need the signing key bytes right now, before the DI
    // container that would resolve IOptions<JwtOptions> even exists yet. Same underlying
    // configuration source either way, so the two can never drift apart (see JwtOptions's own doc
    // comment).
    var jwtOptions = builder.Configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>();
    if (jwtOptions is null || string.IsNullOrWhiteSpace(jwtOptions.SigningKey))
    {
        throw new InvalidOperationException(
            $"Missing '{JwtOptions.SectionName}:SigningKey'. Set it via user-secrets or environment variables in production — see appsettings.json's placeholder for local dev.");
    }

    builder.Services
        .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
        .AddJwtBearer(options =>
        {
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true,
                ValidIssuer = jwtOptions.Issuer,
                ValidateAudience = true,
                ValidAudience = jwtOptions.Audience,
                ValidateIssuerSigningKey = true,
                IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtOptions.SigningKey)),
                ValidateLifetime = true,
                // 15-minute access tokens (security.md) are short enough that a generous clock-skew
                // allowance would meaningfully extend a token's effective life past its stated expiry
                // — keep this small rather than the 5-minute framework default.
                ClockSkew = TimeSpan.FromSeconds(30),
            };
        });

    // "AdminOnly"/"InstructorOnly" (P0-22) — see AuthorizationPolicyExtensions' own doc comment for
    // exactly which roles satisfy each policy and why, plus why CourseOwner/EnrolledInCourse (also
    // listed in ARCHITECTURE.md §2's AuthZ row) aren't defined yet. Pulled into its own extension
    // method rather than an inline options lambda here (unlike AddCors/AddRateLimiter above) so unit
    // tests can register the exact same policies and assert on them via IAuthorizationService.
    builder.Services.AddSiriAuthorizationPolicies();

    builder.Services
        .AddPersistence(builder.Configuration)
        // Shared IConnectionMultiplexer for every Redis consumer (Identity's session mirror, Catalog's
        // category-tree cache, ...) — must run before any module that resolves IConnectionMultiplexer.
        .AddSharedRedis(builder.Configuration)
        .AddHangfireClient(builder.Configuration)
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

    builder.Services.AddControllers(options =>
    {
        options.Filters.Add<ValidationActionFilter>();
    })
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter());
    });

    // P7-12: Fast-fail validation of production secrets and critical configurations
    ProductionConfigurationGuard.ValidateProductionConfiguration(builder.Configuration, builder.Environment);
    ProductionConfigurationGuard.ValidateDeploymentConfiguration(builder.Configuration, builder.Environment);

    var app = builder.Build();

    // Explicit operator command; normal API startup never applies schema changes.
    if (args.Contains("--migrate", StringComparer.OrdinalIgnoreCase))
    {
        await app.Services.ApplyDatabaseMigrationsAsync(CancellationToken.None);
        Log.Information("Database migrations completed successfully.");
        return;
    }

    // ---- One-off dev/test bootstrap (tasks P0-37, P1-30) -----------------------------------
    // `dotnet run --project backend/src/Siri.Api -- --seed` runs IdentitySeeder then CatalogSeeder
    // against whatever ConnectionStrings:Default currently points at (a future local Docker DB, or
    // the current remote one — see each seeder's own doc comment) and exits, instead of starting the
    // web server / Hangfire server / background hosted services below (none of those are started by
    // Build() above, only by Run()/StartAsync(), which this path never reaches). CatalogSeeder always
    // runs after IdentitySeeder, in the same scope: it needs the 5 seeded Instructor accounts' real
    // user ids, which only IdentitySeeder's return value can supply (see CatalogSeeder's own doc
    // comment for why Catalog cannot look these up itself).
    if (args.Contains("--seed", StringComparer.OrdinalIgnoreCase))
    {
        if (app.Environment.IsProduction())
        {
            Log.Fatal("Seeding is strictly forbidden in Production environment.");
            throw new InvalidOperationException("Seeding is strictly forbidden in Production environment.");
        }

        await using var seedScope = app.Services.CreateAsyncScope();

        var identitySeeder = seedScope.ServiceProvider.GetRequiredService<IdentitySeeder>();
        var userIdsByEmail = await identitySeeder.SeedAsync(CancellationToken.None);

        var catalogSeeder = seedScope.ServiceProvider.GetRequiredService<CatalogSeeder>();
        await catalogSeeder.SeedAsync(userIdsByEmail, CancellationToken.None);

        var mediaSeeder = seedScope.ServiceProvider.GetRequiredService<MediaSeeder>();
        var firstInstructorUserId = userIdsByEmail.Values.FirstOrDefault();
        await mediaSeeder.SeedAsync(firstInstructorUserId, CancellationToken.None);

        return;
    }

    // ---- Pipeline -------------------------------------------------------------------------

    app.UseExceptionHandler();

    // P7-02: Enforce security headers (HSTS, CSP, X-Frame-Options, X-Content-Type-Options)
    app.UseMiddleware<SecurityHeadersMiddleware>();

    app.UseMiddleware<CorrelationIdMiddleware>();

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
        app.UseSwaggerUI(options =>
        {
            options.SwaggerEndpoint("/openapi/v1.json", "SIRI UpSkill API v1");
            options.RoutePrefix = "swagger";
        });

        var sampleVideoEnabled = builder.Configuration.GetValue("SIRI_DEV_SAMPLE_VIDEO", false);

        // Guards against a deployed container (QA/Production) that ends up with
        // ASPNETCORE_ENVIRONMENT=Development by misconfiguration: IsDevelopment() alone can't be
        // trusted to mean "this is my native Windows dev machine" — DOTNET_RUNNING_IN_CONTAINER is
        // set automatically by the aspnet base image regardless of that override, so it stays the
        // reliable signal. Without this, this block would try to reach the native dev-only Postgres
        // instance (127.0.0.1:5433, see README.md) from inside the deployed container and fail with a
        // noisy connection-refused warning — this happened for real on QA.
        var runsInContainer = ProductionConfigurationGuard.IsRunningInContainer(builder.Configuration);
        if (sampleVideoEnabled && !runsInContainer)
        {
            // Ensure native dev automatically has Bunny Stream sample media asset linked.
            try
            {
                await using var devSeedScope = app.Services.CreateAsyncScope();
                var devMediaSeeder = devSeedScope.ServiceProvider.GetService<MediaSeeder>();
                if (devMediaSeeder is not null)
                {
                    await devMediaSeeder.SeedAsync(Guid.Empty, CancellationToken.None);
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Dev startup media auto-link note: {Message}", ex.Message);
            }
        }
    }

    app.UseCors("Default");

    app.UseAuthentication();
    app.UseAuthorization();

    // P1-07: activates output caching for endpoints that opt in via .CacheOutput(...) (currently just
    // Catalog's public course search/detail reads) — placed after UseAuthorization per the standard
    // ASP.NET Core ordering so a cached response can never bypass an authorization check. The actual
    // policy (TTL, tag) is module-owned — see Siri.Modules.Catalog.CatalogModule's own AddOutputCache call.
    app.UseOutputCache();

    app.UseRateLimiter();

    app.MapHealthChecks("/health");

    // ---- Background jobs (Hangfire Dashboard) -----------------------------------------------

    app.UseHangfireDashboard("/hangfire", new DashboardOptions
    {
        // See LocalhostOnlyDashboardAuthorizationFilter's own doc comment (updated by P0-22): this is
        // still a deliberate, interim guard, not a full access-control boundary — it now also accepts
        // a valid JWT bearer token carrying the Admin/SuperAdmin role claim (this middleware runs
        // after app.UseAuthentication() above, so HttpContext.User is already populated from any
        // bearer token on the request by the time this filter runs), on top of the original
        // localhost-only check. Pure browser-based remote admin access is still not role-gated — see
        // the filter's doc comment for the known remaining gap.
        Authorization = [new LocalhostOnlyDashboardAuthorizationFilter()],
    });

    app.MapControllers();

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    // HostAbortedException is thrown by `dotnet ef` design-time tooling, which builds the host
    // just far enough to read services then intentionally aborts — not a real startup failure.
    Log.Fatal(ex, "Siri.Api terminated unexpectedly during startup");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

/// <summary>Exposes the generated <c>Program</c> class for <c>WebApplicationFactory&lt;Program&gt;</c> in integration tests.</summary>
public partial class Program;
