using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Siri.Persistence.Interceptors;
using Siri.SharedKernel;

namespace Siri.Persistence.DependencyInjection;

public static class PersistenceServiceCollectionExtensions
{
    public static string NormalizePostgreSqlConnectionString(string connectionString)
    {
        var normalized = System.Text.RegularExpressions.Regex.Replace(
            connectionString,
            @"(?i)(^|;)\s*Connect\s+Timeout\s*=",
            "$1Timeout=");

        return System.Text.RegularExpressions.Regex.Replace(
            normalized,
            @"(?i)(^|;)\s*Server\s*=\s*([^;,\s]+)\s*,\s*(\d+)\s*(?=;|$)",
            "$1Host=$2;Port=$3");
    }

    /// <summary>Applies checked-in migrations for the explicit operator command, without starting the host.</summary>
    public static async Task ApplyDatabaseMigrationsAsync(this IServiceProvider services, CancellationToken cancellationToken)
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        await database.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// Registers <see cref="AppDbContext"/> against PostgreSQL (Npgsql) plus the cross-cutting interceptor
    /// stack. Reads <c>ConnectionStrings:Default</c> — appsettings only carries a non-secret
    /// placeholder value; real connection strings come from user-secrets/env per the security rules.
    /// </summary>
    public static IServiceCollection AddPersistence(this IServiceCollection services, IConfiguration configuration)
    {
        services.TryAddSingleton<IClock, SystemClock>();
        services.TryAddScoped<IUserContext, AnonymousUserContext>();

        // Encryption-at-rest for sensitive financial/identity fields (bank account numbers, tax ids —
        // currently consumed by Siri.Modules.Payout). Registered here, not in a module's own composition
        // root, because it's a SharedKernel-level primitive with no natural single-module owner — same
        // reasoning IClock/IUserContext above already establish.
        services.AddOptions<DataProtectionOptions>()
            .Bind(configuration.GetSection(DataProtectionOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.TryAddSingleton<ISensitiveDataProtector, SensitiveDataProtector>();

        services.AddScoped<AuditableEntityInterceptor>();
        services.AddScoped<ConcurrencyTokenInterceptor>();

        services.AddDbContext<AppDbContext>((serviceProvider, optionsBuilder) =>
        {
            var connectionString = configuration.GetConnectionString("Default");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                connectionString = Environment.GetEnvironmentVariable("ConnectionStrings__Default")
                                   ?? Environment.GetEnvironmentVariable("DATABASE_CONNECTION_STRING");
            }
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Missing 'ConnectionStrings:Default'. Set it via user-secrets or environment variables (.env) — never in appsettings.json.");
            }

            connectionString = NormalizePostgreSqlConnectionString(connectionString);

            optionsBuilder
                .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
                // Order matters: AuditableEntityInterceptor rewrites a delete of an ISoftDelete
                // entity into a Modified entry, so ConcurrencyTokenInterceptor must run after it to
                // rotate that row's token too (see that interceptor's own doc comment).
                .AddInterceptors(
                    serviceProvider.GetRequiredService<AuditableEntityInterceptor>(),
                    serviceProvider.GetRequiredService<ConcurrencyTokenInterceptor>());
        });

        return services;
    }
}
