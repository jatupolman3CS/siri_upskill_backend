using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Siri.Modules.Analytics.Application;
using Siri.Modules.Analytics.Features.AdminDashboardSummary;
using Siri.Modules.Analytics.Features.InstructorAnalytics;
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
        services.AddScoped<GetInstructorAnalyticsHandler>();

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

        var instructorGroup = group.MapGroup("/instructor").RequireAuthorization(AuthorizationPolicyNames.InstructorOnly);

        instructorGroup.MapGet("/dashboard/summary", async (
            [FromQuery] string? range,
            [FromQuery] Guid? courseId,
            GetInstructorAnalyticsHandler handler,
            IUserContext userContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            if (!userContext.UserId.HasValue)
            {
                return Results.Unauthorized();
            }

            var result = await handler.HandleAsync(userContext.UserId.Value, range, courseId, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess
                ? Results.Ok(result.Value)
                : result.Error.ToProblemHttpResult(httpContext);
        })
        .WithName("GetInstructorAnalyticsSummary")
        .WithSummary("ดึงข้อมูลสถิติและการวิเคราะห์สำหรับผู้สอน")
        .Produces<InstructorAnalyticsResponse>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized);

        return endpoints;
    }
}
