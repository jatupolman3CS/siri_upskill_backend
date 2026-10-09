using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Siri.Modules.Catalog.Infrastructure.Bootstrap;
using Siri.Modules.Identity.Infrastructure.Bootstrap;

namespace Siri.Api.Bootstrap;

public static class OwnerBootstrapServiceCollectionExtensions
{
    /// <summary>Registers the owner bootstrap (<c>Identity:Bootstrap:OwnerEmails</c>): its options, the two per-module bootstrappers and the hosted service
    /// that runs them. Needs <c>AddPersistence</c> (AppDbContext, IClock) to have been registered. Empty option = the hosted service does nothing.</summary>
    public static IServiceCollection AddOwnerBootstrap(this IServiceCollection services, IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(services);
        ArgumentNullException.ThrowIfNull(configuration);

        services.AddOptions<OwnerBootstrapOptions>()
            .Bind(configuration.GetSection(OwnerBootstrapOptions.SectionName));

        services.AddScoped<OwnerAccountBootstrapper>();
        services.AddScoped<OwnerInstructorProfileBootstrapper>();
        services.AddHostedService<OwnerBootstrapService>();

        return services;
    }
}
