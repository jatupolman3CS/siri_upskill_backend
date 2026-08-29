using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Cms.Application;

public static class FeatureFlagEndpoints
{
    public static IEndpointRouteBuilder MapFeatureFlagEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var publicGroup = endpoints.MapGroup("/api/cms/feature-flags").WithTags("Cms");

        publicGroup.MapGet("/", async (
            FeatureFlagService service,
            CancellationToken cancellationToken) =>
        {
            var flags = await service.GetEnabledFlagsAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(flags);
        })
        .WithName("GetPublicFeatureFlags")
        .WithSummary("ดึงรายการ Feature Flags ที่เปิดใช้งาน")
        .AllowAnonymous()
        .Produces<IReadOnlyList<FeatureFlagResponse>>(StatusCodes.Status200OK);

        var adminGroup = endpoints.MapGroup("/api/cms/admin/feature-flags")
            .WithTags("Cms")
            .RequireAuthorization(AuthorizationPolicyNames.AdminOnly);

        adminGroup.MapGet("/", async (
            FeatureFlagService service,
            CancellationToken cancellationToken) =>
        {
            var flags = await service.GetAllFlagsAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(flags);
        })
        .WithName("GetAdminFeatureFlags")
        .WithSummary("ดึงรายการ Feature Flags ทั้งหมดสำหรับผู้ดูแลระบบ")
        .Produces<IReadOnlyList<FeatureFlagResponse>>(StatusCodes.Status200OK);

        adminGroup.MapPut("/{key}", async (
            string key,
            [FromBody] UpsertFeatureFlagRequest request,
            FeatureFlagService service,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await service.UpsertFlagAsync(key, request, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
        })
        .WithName("UpsertFeatureFlag")
        .WithSummary("สร้างหรืออัปเดตสถานะ Feature Flag")
        .Produces<FeatureFlagResponse>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status400BadRequest);

        return endpoints;
    }
}
