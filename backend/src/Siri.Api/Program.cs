using Microsoft.AspNetCore.RateLimiting;
using Serilog;
using Serilog.Formatting.Json;
using Siri.Api.ErrorHandling;
using Siri.Api.Middleware;
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

    builder.Host.UseSerilog((context, services, configuration) => configuration
        .MinimumLevel.Information()
        .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("Application", "Siri.Api")
        .WriteTo.Console(new JsonFormatter()));

    // ---- Services -----------------------------------------------------------------------

    builder.Services.AddOpenApi();

    builder.Services.AddProblemDetails();
    builder.Services.AddExceptionHandler<GlobalExceptionHandler>();

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("Default", policy =>
        {
            var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

            if (allowedOrigins.Length > 0)
            {
                policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod();
            }
            else
            {
                // No frontend origin is finalized yet (see CLAUDE.md "ยังค้าง"/ROADMAP). Wide open
                // for now so local/dev integration isn't blocked; tighten once a real origin exists.
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
            }
        });
    });

    builder.Services.AddRateLimiter(options =>
    {
        options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

        // Placeholders only — not yet applied to any endpoint. Real limits (login, refresh,
        // playback-token, checkout) land with those endpoints per ARCHITECTURE.md §2 "Rate limit".
        options.AddFixedWindowLimiter("auth", limiter =>
        {
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.PermitLimit = 5;
            limiter.QueueLimit = 0;
        });

        options.AddFixedWindowLimiter("default", limiter =>
        {
            limiter.Window = TimeSpan.FromMinutes(1);
            limiter.PermitLimit = 100;
            limiter.QueueLimit = 0;
        });
    });

    builder.Services.AddHealthChecks();

    builder.Services
        .AddPersistence(builder.Configuration)
        .AddIdentityModule()
        .AddCatalogModule()
        .AddMediaModule()
        .AddLearningModule()
        .AddCommerceModule()
        .AddPayoutModule()
        .AddCmsModule()
        .AddCommunityModule()
        .AddNotificationModule()
        .AddAnalyticsModule();

    var app = builder.Build();

    // ---- Pipeline -------------------------------------------------------------------------

    app.UseExceptionHandler();

    app.UseMiddleware<CorrelationIdMiddleware>();

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseCors("Default");

    app.UseRateLimiter();

    app.MapHealthChecks("/health");

    app
        .MapIdentityEndpoints()
        .MapCatalogEndpoints()
        .MapMediaEndpoints()
        .MapLearningEndpoints()
        .MapCommerceEndpoints()
        .MapPayoutEndpoints()
        .MapCmsEndpoints()
        .MapCommunityEndpoints()
        .MapNotificationEndpoints()
        .MapAnalyticsEndpoints();

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
