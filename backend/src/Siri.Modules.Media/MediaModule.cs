using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Siri.Modules.Media;

/// <summary>
/// Composition root for the Media module. Everything the module exposes to <c>Siri.Api</c> goes
/// through these two extension methods — no other public surface is wired into the host.
/// Empty for now (skeleton phase); business logic, endpoints and DI registrations land with the
/// Media module's feature work.
/// </summary>
public static class MediaModule
{
    /// <summary>Registers the Media module's services (handlers, options, infrastructure) into the container.</summary>
    public static IServiceCollection AddMediaModule(this IServiceCollection services)
    {
        return services;
    }

    /// <summary>Maps the Media module's minimal API endpoints onto the host's route builder.</summary>
    public static IEndpointRouteBuilder MapMediaEndpoints(this IEndpointRouteBuilder endpoints)
    {
        return endpoints;
    }
}
