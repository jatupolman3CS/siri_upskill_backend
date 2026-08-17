using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Siri.Modules.Commerce;

/// <summary>
/// Composition root for the Commerce module. Everything the module exposes to <c>Siri.Api</c> goes
/// through these two extension methods — no other public surface is wired into the host.
/// Empty for now (skeleton phase); business logic, endpoints and DI registrations land with the
/// Commerce module's feature work.
/// </summary>
public static class CommerceModule
{
    /// <summary>Registers the Commerce module's services (handlers, options, infrastructure) into the container.</summary>
    public static IServiceCollection AddCommerceModule(this IServiceCollection services)
    {
        return services;
    }

    /// <summary>Maps the Commerce module's minimal API endpoints onto the host's route builder.</summary>
    public static IEndpointRouteBuilder MapCommerceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        return endpoints;
    }
}
