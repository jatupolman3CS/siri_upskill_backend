using System.Text;
using Hangfire;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Formatting.Json;
using Siri.Api.Authorization;
using Siri.Api.ErrorHandling;
using Siri.Api.Middleware;
using Siri.Modules.Analytics;
using Siri.Modules.Catalog;
using Siri.Modules.Cms;
using Siri.Modules.Commerce;
using Siri.Modules.Community;
using Siri.Modules.Identity;
using Siri.Modules.Identity.Infrastructure;
using Siri.Modules.Identity.Infrastructure.Seeding;
using Siri.Modules.Learning;
using Siri.Modules.Media;
using Siri.Modules.Notification;
using Siri.Modules.Payout;
using Siri.Persistence.DependencyInjection;
using Siri.Workers;

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
                // AllowCredentials is required for the httpOnly refresh-token cookie (security.md /
                // RefreshTokenCookie.cs) to be set/sent on cross-origin requests from the Angular dev
                // server. Only ever paired with an explicit origin list - AllowAnyOrigin +
                // AllowCredentials is invalid per the CORS spec (browsers reject it outright), so the
                // wildcard branch below must never call AllowCredentials.
                policy.WithOrigins(allowedOrigins).AllowAnyHeader().AllowAnyMethod().AllowCredentials();
            }
            else
            {
                // No frontend origin is finalized for this environment (e.g. production domain not
                // decided yet - see CLAUDE.md "ยังค้าง"/ROADMAP). Wide open for browsing, but credentialed
                // requests (login/refresh, which set/read the httpOnly cookie) will not work until a
                // real origin is configured via Cors:AllowedOrigins - that's a real, known limitation of
                // this fallback, not an oversight; tighten by setting the config once an origin exists.
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
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
            limiter.PermitLimit = 5;
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
    });

    builder.Services.AddHealthChecks();

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
        .AddWorkers(builder.Configuration)
        .AddIdentityModule(builder.Configuration)
        .AddCatalogModule()
        .AddMediaModule()
        .AddLearningModule()
        .AddCommerceModule()
        .AddPayoutModule()
        .AddCmsModule()
        .AddCommunityModule()
        .AddNotificationModule(builder.Configuration)
        .AddAnalyticsModule();

    var app = builder.Build();

    // ---- One-off dev/test bootstrap (task P0-37) ------------------------------------------
    // `dotnet run --project backend/src/Siri.Api -- --seed` runs IdentitySeeder against whatever
    // ConnectionStrings:Default currently points at (a future local Docker DB, or the current
    // remote one — see IdentitySeeder's own doc comment) and exits, instead of starting the web
    // server / Hangfire server / background hosted services below (none of those are started by
    // Build() above, only by Run()/StartAsync(), which this path never reaches).
    if (args.Contains("--seed", StringComparer.OrdinalIgnoreCase))
    {
        await using var seedScope = app.Services.CreateAsyncScope();
        var seeder = seedScope.ServiceProvider.GetRequiredService<IdentitySeeder>();
        await seeder.SeedAsync(CancellationToken.None);
        return;
    }

    // ---- Pipeline -------------------------------------------------------------------------

    app.UseExceptionHandler();

    app.UseMiddleware<CorrelationIdMiddleware>();

    app.UseSerilogRequestLogging();

    if (app.Environment.IsDevelopment())
    {
        app.MapOpenApi();
    }

    app.UseCors("Default");

    app.UseAuthentication();
    app.UseAuthorization();

    app.UseRateLimiter();

    app.MapHealthChecks("/health");

    // ---- Background jobs (Hangfire) --------------------------------------------------------

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

    app.Services.GetRequiredService<IRecurringJobManager>().MapRecurringJobs();

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
