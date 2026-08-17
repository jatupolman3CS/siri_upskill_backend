using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Siri.Modules.Community;

/// <summary>
/// Composition root for the Community module. Everything the module exposes to <c>Siri.Api</c> goes
/// through these two extension methods — no other public surface is wired into the host.
/// Empty for now (skeleton phase); business logic, endpoints and DI registrations land with the
/// Community module's feature work.
/// </summary>
public static class CommunityModule
{
    /// <summary>Registers the Community module's services (handlers, options, infrastructure) into the container.</summary>
    public static IServiceCollection AddCommunityModule(this IServiceCollection services)
    {
        return services;
    }

    /// <summary>Maps the Community module's minimal API endpoints onto the host's route builder.</summary>
    public static IEndpointRouteBuilder MapCommunityEndpoints(this IEndpointRouteBuilder endpoints)
    {
        return endpoints;
    }
}
