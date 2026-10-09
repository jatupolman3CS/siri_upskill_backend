using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Siri.Modules.Notification.Infrastructure.Delivery;
using Siri.Persistence.DependencyInjection;

namespace Siri.Workers;

public static class WorkersServiceCollectionExtensions
{
    /// <summary>
    /// Registers Hangfire PostgreSQL storage without starting the processing server (AddHangfireServer).
    /// Used by API host (Siri.Api) to allow enqueuing background jobs and serving the Hangfire Dashboard
    /// without consuming resources executing jobs in the web process.
    /// </summary>
    public static IServiceCollection AddHangfireClient(this IServiceCollection services, IConfiguration configuration)
    {
        return services.AddHangfireStorage(configuration);
    }

    /// <summary>
    /// Registers Hangfire for the web host (<c>Siri.Api</c>): storage always (enqueue + dashboard), and — when <c>Hangfire:ServerInApi</c> is on
    /// (<see cref="HangfireHostingOptions"/>, default <c>true</c>) — the <b>same</b> processing server the dedicated Workers host runs
    /// (<see cref="AddHangfireWorker"/>: identical queues and worker-count defaults, so the two can never drift apart) plus the scheduling of the same
    /// recurring jobs under the same ids (<see cref="RecurringJobsRegistrationService"/>). That makes a single-container deployment complete on its own;
    /// a dedicated Workers deployment can still run alongside it — see <c>docs/DEPLOYMENT.md</c> for why that is safe.
    /// </summary>
    public static IServiceCollection AddHangfireForApi(
        this IServiceCollection services,
        IConfiguration configuration,
        IHostEnvironment environment)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(environment);

        var serverInApi = HangfireHostingOptions.ResolveServerInApi(configuration, environment);

        services.AddOptions<HangfireHostingOptions>()
            .Bind(configuration.GetSection(HangfireHostingOptions.SectionName))
            .PostConfigure(options => options.ServerInApi = serverInApi) // the effective value (IntegrationTest default etc.)
            .ValidateOnStart();

        if (!serverInApi)
        {
            return services.AddHangfireClient(configuration);
        }

        // A recognisable name in the dashboard and in the admin status: "api:<host>" next to the Workers host's plain "<host>".
        services.AddHangfireWorker(configuration, server => server.ServerName = $"api:{Environment.MachineName.ToLowerInvariant()}");
        services.AddHostedService<RecurringJobsRegistrationService>();

        // Email delivery moves with the Hangfire server: when the notification transport is Kafka the e-mail sender job stands down
        // wherever it runs, so the relays and consumers must run wherever the server does — otherwise a single-container deployment
        // (this branch) would stop sending mail. No-op for the default Database transport. Running it in a Workers host as well is
        // safe by construction (relays claim rows with SKIP LOCKED, consumers share a group and a Redis claim).
        services.AddNotificationDelivery(configuration);

        return services;
    }

    /// <summary>
    /// Registers Hangfire PostgreSQL storage AND starts the background job processing server (AddHangfireServer).
    /// Used by the standalone worker host (Siri.Workers), and by <see cref="AddHangfireForApi"/> when the API hosts the server itself.
    /// </summary>
    public static IServiceCollection AddHangfireWorker(
        this IServiceCollection services,
        IConfiguration configuration,
        Action<BackgroundJobServerOptions>? configureServer = null)
    {
        services.AddHangfireStorage(configuration);

        if (configureServer != null)
        {
            services.AddHangfireServer(configureServer);
        }
        else
        {
            services.AddHangfireServer();
        }

        return services;
    }

    /// <summary>
    /// Legacy compatibility helper: registers Hangfire storage and server.
    /// </summary>
    public static IServiceCollection AddWorkers(this IServiceCollection services, IConfiguration configuration)
    {
        return services.AddHangfireWorker(configuration);
    }

    /// <summary>
    /// Registers Hangfire against PostgreSQL storage (see ARCHITECTURE.md — "Jobs: Hangfire
    /// (PostgreSQL storage)"). Reads <c>ConnectionStrings:Default</c>, same placeholder-in-appsettings
    /// / real-value-in-user-secrets convention as <c>AddPersistence</c>.
    /// </summary>
    public static IServiceCollection AddHangfireStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = PersistenceServiceCollectionExtensions.GetDefaultConnectionString(configuration);
        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            // The (string, options) overload is [Obsolete] in Hangfire.PostgreSql 1.21.1 ("will be
            // removed in 2.0"), and this repo builds with TreatWarningsAsErrors — so the bootstrapper
            // lambda is the only form that compiles, not merely the preferred one.
            .UsePostgreSqlStorage(pg => pg.UseNpgsqlConnection(connectionString), new PostgreSqlStorageOptions
            {
                // Own schema — not "public", and not any of the app's own per-module schemas — so
                // Hangfire's internal tables never collide with application tables, consistent with
                // this codebase's per-module schema convention (database.md: "Schema แยกตาม
                // module"). Hangfire creates/migrates these tables itself on startup
                // (PrepareSchemaIfNecessary); they are NOT part of the app's own EF Core migrations.
                //
                // Lowercase on purpose, unlike the app's own UPPERCASE schemas (P0-41): this schema
                // belongs to Hangfire, not to us, and PostgreSQL folds unquoted identifiers to
                // lowercase — an uppercase name here would have to be quoted in every hand-written
                // query against Hangfire's tables for no benefit.
                SchemaName = "hangfire",
                PrepareSchemaIfNecessary = true,
            })
            // Job retention (task P0-23): how long a *finished* job's row survives before Hangfire's
            // background expiration sweep deletes it. Hangfire's own default is 24 hours; 7 days
            // gives enough time to notice and investigate a failed run (e.g. the email outbox
            // sender) over a weekend without letting the job-history tables grow unbounded.
            .WithJobExpirationTimeout(TimeSpan.FromDays(7)));

        // Read-only monitoring view for the admin status endpoint (JobStorage itself is registered by AddHangfire above).
        services.TryAddSingleton<IBackgroundJobStatusReader, HangfireBackgroundJobStatusReader>();

        return services;
    }
}
