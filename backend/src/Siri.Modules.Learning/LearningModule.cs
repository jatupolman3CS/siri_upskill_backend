using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Siri.Modules.Learning;

/// <summary>
/// Composition root for the Learning module. Everything the module exposes to <c>Siri.Api</c> goes
/// through these two extension methods — no other public surface is wired into the host.
/// Empty for now (skeleton phase); business logic, endpoints and DI registrations land with the
/// Learning module's feature work.
/// </summary>
public static class LearningModule
{
    /// <summary>Registers the Learning module's services (handlers, options, infrastructure) into the container.</summary>
    public static IServiceCollection AddLearningModule(this IServiceCollection services)
    {
        return services;
    }

    /// <summary>Maps the Learning module's minimal API endpoints onto the host's route builder.</summary>
    public static IEndpointRouteBuilder MapLearningEndpoints(this IEndpointRouteBuilder endpoints)
    {
        return endpoints;
    }
}
