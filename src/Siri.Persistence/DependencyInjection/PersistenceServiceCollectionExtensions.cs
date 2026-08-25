using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Siri.Persistence.Interceptors;
using Siri.SharedKernel;

namespace Siri.Persistence.DependencyInjection;

public static class PersistenceServiceCollectionExtensions
{
    /// <summary>
    /// Registers <see cref="AppDbContext"/> against SQL Server plus the cross-cutting interceptor
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

        services.AddDbContext<AppDbContext>((serviceProvider, optionsBuilder) =>
        {
            var connectionString = configuration.GetConnectionString("Default");
            if (string.IsNullOrWhiteSpace(connectionString))
            {
                throw new InvalidOperationException(
                    "Missing 'ConnectionStrings:Default'. Set it via user-secrets or environment variables — never in appsettings.json.");
            }

            optionsBuilder
                .UseSqlServer(connectionString, sql => sql.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName))
                .AddInterceptors(serviceProvider.GetRequiredService<AuditableEntityInterceptor>());
        });

        return services;
    }
}
