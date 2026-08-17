using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Siri.Modules.Analytics;

/// <summary>
/// Composition root for the Analytics module. Everything the module exposes to <c>Siri.Api</c> goes
/// through these two extension methods — no other public surface is wired into the host.
/// Empty for now (skeleton phase); business logic, endpoints and DI registrations land with the
/// Analytics module's feature work.
/// </summary>
public static class AnalyticsModule
{
    /// <summary>Registers the Analytics module's services (handlers, options, infrastructure) into the container.</summary>
    public static IServiceCollection AddAnalyticsModule(this IServiceCollection services)
    {
        return services;
    }

    /// <summary>Maps the Analytics module's minimal API endpoints onto the host's route builder.</summary>
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        return endpoints;
    }
}
