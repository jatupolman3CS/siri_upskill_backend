using Hangfire;
using Hangfire.PostgreSql;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    /// Registers Hangfire PostgreSQL storage AND starts the background job processing server (AddHangfireServer).
    /// Used by the standalone worker host (Siri.Workers).
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

        return services;
    }
}
