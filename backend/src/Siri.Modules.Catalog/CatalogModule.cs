using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Siri.Modules.Catalog;

/// <summary>
/// Composition root for the Catalog module. Everything the module exposes to <c>Siri.Api</c> goes
/// through these two extension methods — no other public surface is wired into the host.
/// Empty for now (skeleton phase); business logic, endpoints and DI registrations land with the
/// Catalog module's feature work.
/// </summary>
public static class CatalogModule
{
    /// <summary>Registers the Catalog module's services (handlers, options, infrastructure) into the container.</summary>
    public static IServiceCollection AddCatalogModule(this IServiceCollection services)
    {
        return services;
    }

    /// <summary>Maps the Catalog module's minimal API endpoints onto the host's route builder.</summary>
    public static IEndpointRouteBuilder MapCatalogEndpoints(this IEndpointRouteBuilder endpoints)
    {
        return endpoints;
    }
}
