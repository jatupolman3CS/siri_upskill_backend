using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

/// <summary>
/// Endpoints for <see cref="BUNDLE"/> — split across two mapping methods because this aggregate spans two
/// policy tiers: public storefront browsing (<see cref="MapBundleEndpoints"/>, <c>AllowAnonymous()</c> on
/// top of the module's default-deny group — same "retrofit default-deny then allow-anonymous per route"
/// shape <c>IdentityModule</c>'s public endpoints already use) vs. admin authoring
/// (<see cref="MapAdminBundleEndpoints"/>, <c>AdminOnly</c>).
/// </summary>
public static class BundleEndpoints
{
    /// <summary>Maps GET /api/commerce/bundles/{bundleId} and GET /api/commerce/bundles — both public,
    /// unauthenticated (mirrors Catalog's public course/category browsing).</summary>
    public static IEndpointRouteBuilder MapBundleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/{bundleId:guid}", GetByIdAsync)
            .AllowAnonymous()
            .WithName("CommerceGetBundle")
            .WithSummary("ดูรายละเอียดชุดคอร์ส")
            .Produces<BundleResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        endpoints.MapGet("/", ListAsync)
            .AllowAnonymous()
            .WithName("CommerceListBundles")
            .WithSummary("รายการชุดคอร์สทั้งหมด")
            .Produces<PagedResult<BundleResponse>>(StatusCodes.Status200OK);

        endpoints.MapGet("/by-course/{courseId:guid}", async (Guid courseId, BundleService bundleService, CancellationToken cancellationToken) =>
        {
            var result = await bundleService.GetBundlesByCourseIdAsync(courseId, cancellationToken).ConfigureAwait(false);
            return Results.Ok(result);
        })
        .AllowAnonymous()
        .WithName("CommerceGetBundlesByCourse")
        .WithSummary("ดึงรายการชุดคอร์สที่เกี่ยวข้องกับคอร์สนี้ (Frequently Bought Together)")
        .Produces<IReadOnlyList<BundleResponse>>(StatusCodes.Status200OK);

        return endpoints;
    }

    /// <summary>Maps POST /api/commerce/admin/bundles.</summary>
    public static IEndpointRouteBuilder MapAdminBundleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/", CreateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreateBundleCommand>>()
            .WithName("CommerceCreateBundle")
            .WithSummary("สร้างชุดคอร์สใหม่")
            .Produces<BundleResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> GetByIdAsync(Guid bundleId, BundleService bundleService, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var result = await bundleService.GetByIdAsync(bundleId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> ListAsync(BundleService bundleService, CancellationToken cancellationToken, int page = 1, int pageSize = 20)
    {
        var result = await bundleService.ListAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateAsync(CreateBundleCommand command, BundleService bundleService, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var result = await bundleService.CreateAsync(command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Created($"/api/commerce/bundles/{result.Value.Id}", result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }
}
