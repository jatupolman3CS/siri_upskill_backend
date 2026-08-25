using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Siri.Modules.Analytics.Application;
using Siri.Modules.Analytics.Features.AdminDashboardSummary;
using Siri.Modules.Analytics.Infrastructure;
using Siri.SharedKernel;

namespace Siri.Modules.Analytics;

/// <summary>
/// Composition root for the Analytics module.
/// </summary>
public static class AnalyticsModule
{
    /// <summary>Registers the Analytics module's services into the container.</summary>
    public static IServiceCollection AddAnalyticsModule(this IServiceCollection services)
    {
        services.AddScoped<IDailyCourseStatRepository, DailyCourseStatRepository>();
        services.AddScoped<IEpisodeDropOffRepository, EpisodeDropOffRepository>();
        services.AddScoped<AnalyticsRollupJob>();

        // Features
        services.AddScoped<GetAdminDashboardSummaryHandler>();

        return services;
    }

    /// <summary>Maps the Analytics module's minimal API endpoints onto the host's route builder.</summary>
    public static IEndpointRouteBuilder MapAnalyticsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/analytics").WithTags("Analytics").RequireAuthorization();

        var adminGroup = group.MapGroup("/admin").RequireAuthorization(AuthorizationPolicyNames.AdminOnly);

        adminGroup.MapGet("/dashboard/summary", async (
            GetAdminDashboardSummaryHandler handler,
            CancellationToken cancellationToken) =>
        {
            var summary = await handler.HandleAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(summary);
        })
        .WithName("GetAdminDashboardSummary")
        .WithSummary("ดึงข้อมูลสรุปแดชบอร์ดสำหรับผู้ดูแลระบบ")
        .Produces<AdminDashboardSummaryResponse>(StatusCodes.Status200OK);

        return endpoints;
    }
}
