using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Siri.Workers;

public static class WorkersServiceCollectionExtensions
{
    /// <summary>
    /// Registers Hangfire against SQL Server storage (see ARCHITECTURE.md — "Jobs: Hangfire
    /// (SQL Server storage)"). Reads <c>ConnectionStrings:Default</c>, same placeholder-in-appsettings
    /// / real-value-in-user-secrets convention as <c>AddPersistence</c>.
    /// </summary>
    public static IServiceCollection AddWorkers(this IServiceCollection services, IConfiguration configuration)
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
            .UseSqlServerStorage(connectionString));

        services.AddHangfireServer();

        return services;
    }
}
