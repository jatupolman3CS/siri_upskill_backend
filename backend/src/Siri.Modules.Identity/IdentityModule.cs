using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Siri.Modules.Identity;

/// <summary>
/// Composition root for the Identity module. Everything the module exposes to <c>Siri.Api</c> goes
/// through these two extension methods — no other public surface is wired into the host.
/// Empty for now (skeleton phase); business logic, endpoints and DI registrations land with the
/// Identity module's feature work.
/// </summary>
public static class IdentityModule
{
    /// <summary>Registers the Identity module's services (handlers, options, infrastructure) into the container.</summary>
    public static IServiceCollection AddIdentityModule(this IServiceCollection services)
    {
        return services;
    }

    /// <summary>Maps the Identity module's minimal API endpoints onto the host's route builder.</summary>
    public static IEndpointRouteBuilder MapIdentityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        return endpoints;
    }
}
