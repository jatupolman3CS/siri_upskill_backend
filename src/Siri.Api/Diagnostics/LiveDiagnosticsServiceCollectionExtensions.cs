using Microsoft.Extensions.DependencyInjection;

namespace Siri.Api.Diagnostics;

public static class LiveDiagnosticsServiceCollectionExtensions
{
    /// <summary>Registers the admin Live status service and the one-shot Production startup check. Needs the modules' readers (Live, Notification),
    /// <c>AddHangfireForApi</c> and the Email integration to have been registered.</summary>
    public static IServiceCollection AddLiveDiagnostics(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<LiveAdminStatusService>();
        services.AddHostedService<LiveStatusStartupReporter>();

        return services;
    }
}
