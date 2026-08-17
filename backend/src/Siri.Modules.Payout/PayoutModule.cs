using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Siri.Modules.Payout;

/// <summary>
/// Composition root for the Payout module. Everything the module exposes to <c>Siri.Api</c> goes
/// through these two extension methods — no other public surface is wired into the host.
/// Empty for now (skeleton phase); business logic, endpoints and DI registrations land with the
/// Payout module's feature work.
/// </summary>
public static class PayoutModule
{
    /// <summary>Registers the Payout module's services (handlers, options, infrastructure) into the container.</summary>
    public static IServiceCollection AddPayoutModule(this IServiceCollection services)
    {
        return services;
    }

    /// <summary>Maps the Payout module's minimal API endpoints onto the host's route builder.</summary>
    public static IEndpointRouteBuilder MapPayoutEndpoints(this IEndpointRouteBuilder endpoints)
    {
        return endpoints;
    }
}
