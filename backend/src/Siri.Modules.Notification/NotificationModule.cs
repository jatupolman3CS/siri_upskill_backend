using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;

namespace Siri.Modules.Notification;

/// <summary>
/// Composition root for the Notification module. Everything the module exposes to <c>Siri.Api</c> goes
/// through these two extension methods — no other public surface is wired into the host.
/// Empty for now (skeleton phase); business logic, endpoints and DI registrations land with the
/// Notification module's feature work.
/// </summary>
public static class NotificationModule
{
    /// <summary>Registers the Notification module's services (handlers, options, infrastructure) into the container.</summary>
    public static IServiceCollection AddNotificationModule(this IServiceCollection services)
    {
        return services;
    }

    /// <summary>Maps the Notification module's minimal API endpoints onto the host's route builder.</summary>
    public static IEndpointRouteBuilder MapNotificationEndpoints(this IEndpointRouteBuilder endpoints)
    {
        return endpoints;
    }
}
