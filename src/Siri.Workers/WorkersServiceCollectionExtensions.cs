using Hangfire;
using Hangfire.SqlServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Siri.Workers;

public static class WorkersServiceCollectionExtensions
{
    /// <summary>
    /// Registers Hangfire SQL Server storage without starting the processing server (AddHangfireServer).
    /// Used by API host (Siri.Api) to allow enqueuing background jobs and serving the Hangfire Dashboard
    /// without consuming resources executing jobs in the web process.
    /// </summary>
    public static IServiceCollection AddHangfireClient(this IServiceCollection services, IConfiguration configuration)
    {
        return services.AddHangfireStorage(configuration);
    }

    /// <summary>
    /// Registers Hangfire SQL Server storage AND starts the background job processing server (AddHangfireServer).
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
    /// Registers Hangfire against SQL Server storage (see ARCHITECTURE.md — "Jobs: Hangfire
    /// (SQL Server storage)"). Reads <c>ConnectionStrings:Default</c>, same placeholder-in-appsettings
    /// / real-value-in-user-secrets convention as <c>AddPersistence</c>.
    /// </summary>
    public static IServiceCollection AddHangfireStorage(this IServiceCollection services, IConfiguration configuration)
    {
        var connectionString = configuration.GetConnectionString("Default");
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "Missing 'ConnectionStrings:Default'. Set it via user-secrets or environment variables — never in appsettings.json.");
        }

        services.AddHangfire(config => config
            .SetDataCompatibilityLevel(CompatibilityLevel.Version_180)
            .UseSimpleAssemblyNameTypeSerializer()
            .UseRecommendedSerializerSettings()
            .UseSqlServerStorage(connectionString, new SqlServerStorageOptions
            {
                // Own schema — not "dbo", and not any of the app's own per-module schemas — so
                // Hangfire's internal tables never collide with application tables, consistent with
                // this codebase's per-module schema convention (database.md: "Schema แยกตาม
                // module"). Hangfire creates/migrates these tables itself on startup
                // (PrepareSchemaIfNecessary); they are NOT part of the app's own EF Core migrations.
                SchemaName = "HANGFIRE",
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
