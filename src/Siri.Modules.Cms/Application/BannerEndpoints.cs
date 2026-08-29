using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Cms.Application;

/// <summary>
/// Maps every <see cref="Domain.BANNER"/> HTTP endpoint — one file per entity (matches this module's
/// Repository+Service folder layout, docs/DECISIONS.md D-17), same shape
/// <c>Siri.Modules.Payout.Application.RevenueSplitEndpoints</c>'s own doc comment describes: route
/// groups/policies are applied by the caller (<c>CmsModule.MapCmsEndpoints</c>), each mapping method here
/// only maps its own relative route.
/// <para>
/// Every handler delegate below calls straight into <see cref="BannerService"/>, which is implemented for
/// real and wired into <c>Siri.Api/Program.cs</c> as of 2026-08-24 — the old "scaffold, throws
/// NotImplementedException" note here was stale and has been removed. Per-task completeness is tracked in
/// <c>docs/TASKS.md</c>'s Status column, never in doc comments.
/// </para>
/// </summary>
public static class BannerEndpoints
{
    /// <summary>Maps POST /api/cms/admin/banners.</summary>
    public static IEndpointRouteBuilder MapCreateBannerEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/", HandleCreateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreateBannerCommand>>()
            .WithName("CmsCreateBanner")
            .WithSummary("สร้างแบนเนอร์ใหม่")
            .Produces<BannerResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        return endpoints;
    }

    /// <summary>Maps PUT /api/cms/admin/banners/{id}.</summary>
    public static IEndpointRouteBuilder MapUpdateBannerEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/{id:guid}", HandleUpdateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<UpdateBannerCommand>>()
            .WithName("CmsUpdateBanner")
            .WithSummary("แก้ไขแบนเนอร์")
            .Produces<BannerResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>Maps POST /api/cms/admin/banners/reorder. POST, not PUT/PATCH — bulk action against an
    /// implicit collection (one placement's siblings), not a single addressable resource, same reasoning
    /// <c>Siri.Modules.Catalog.Features.ReorderCategories.ReorderCategoriesEndpoint</c>'s own doc comment
    /// gives.</summary>
    public static IEndpointRouteBuilder MapReorderBannersEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/reorder", HandleReorderAsync)
            .AddEndpointFilter<ValidationEndpointFilter<ReorderBannersCommand>>()
            .WithName("CmsReorderBanners")
            .WithSummary("จัดลำดับแบนเนอร์ภายใน placement เดียวกันใหม่ทั้งหมด")
            .Produces(StatusCodes.Status204NoContent)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>Maps DELETE /api/cms/admin/banners/{id}.</summary>
    public static IEndpointRouteBuilder MapDeleteBannerEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDelete("/{id:guid}", HandleDeleteAsync)
            .WithName("CmsDeleteBanner")
            .WithSummary("ลบแบนเนอร์")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>Maps GET /api/cms/banners (public active banners).</summary>
    public static IEndpointRouteBuilder MapListActiveBannersEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", HandleListActiveAsync)
            .AllowAnonymous()
            .WithName("CmsListActiveBanners")
            .WithSummary("รายการแบนเนอร์ที่เปิดใช้งานสำหรับผู้เข้าชมเว็บ")
            .Produces<IReadOnlyList<BannerResponse>>(StatusCodes.Status200OK);

        return endpoints;
    }

    /// <summary>Maps GET /api/cms/admin/banners?page=&amp;pageSize=.</summary>
    public static IEndpointRouteBuilder MapListBannersEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", HandleListAsync)
            .WithName("CmsListBanners")
            .WithSummary("รายการแบนเนอร์ทั้งหมด (ทุก placement)")
            .Produces<PagedResult<BannerResponse>>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(
        CreateBannerCommand command,
        BannerService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/cms/admin/banners/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleUpdateAsync(
        Guid id,
        UpdateBannerCommand command,
        BannerService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateAsync(id, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleReorderAsync(
        ReorderBannersCommand command,
        BannerService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.ReorderAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleDeleteAsync(
        Guid id,
        BannerService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleListAsync(
        BannerService service,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20)
    {
        var result = await service.ListAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> HandleListActiveAsync(
        BannerService service,
        IClock clock,
        CancellationToken cancellationToken,
        string placement = "HomeHero")
    {
        var result = await service.GetActiveByPlacementAsync(placement, clock.UtcNow, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
}
