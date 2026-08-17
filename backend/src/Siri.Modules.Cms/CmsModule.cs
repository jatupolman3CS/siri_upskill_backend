using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Siri.Modules.Cms;

/// <summary>
/// Composition root for the Cms module. Everything the module exposes to <c>Siri.Api</c> goes
/// through these two extension methods — no other public surface is wired into the host.
/// Empty for now (skeleton phase); business logic, endpoints and DI registrations land with the
/// Cms module's feature work.
/// </summary>
public static class CmsModule
{
    /// <summary>Registers the Cms module's services (handlers, options, infrastructure) into the container.</summary>
    public static IServiceCollection AddCmsModule(this IServiceCollection services)
    {
        return services;
    }

    /// <summary>Maps the Cms module's minimal API endpoints onto the host's route builder.</summary>
    public static IEndpointRouteBuilder MapCmsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        return endpoints;
    }
}
