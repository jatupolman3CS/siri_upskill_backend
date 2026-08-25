using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

/// <summary>Endpoints for <see cref="FLASH_SALE"/> — same public-read/admin-write split as
/// <see cref="BundleEndpoints"/>'s own doc comment.</summary>
public static class FlashSaleEndpoints
{
    /// <summary>Maps GET /api/commerce/flash-sales/{flashSaleId} and GET /api/commerce/flash-sales — both
    /// public, unauthenticated.</summary>
    public static IEndpointRouteBuilder MapFlashSaleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/{flashSaleId:guid}", GetByIdAsync)
            .AllowAnonymous()
            .WithName("CommerceGetFlashSale")
            .WithSummary("ดูรายละเอียดแฟลชเซล")
            .Produces<FlashSaleResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        endpoints.MapGet("/", ListAsync)
            .AllowAnonymous()
            .WithName("CommerceListFlashSales")
            .WithSummary("รายการแฟลชเซลทั้งหมด")
            .Produces<PagedResult<FlashSaleResponse>>(StatusCodes.Status200OK);

        return endpoints;
    }

    /// <summary>Maps POST /api/commerce/admin/flash-sales.</summary>
    public static IEndpointRouteBuilder MapAdminFlashSaleEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/", CreateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreateFlashSaleCommand>>()
            .WithName("CommerceCreateFlashSale")
            .WithSummary("สร้างแฟลชเซลใหม่")
            .Produces<FlashSaleResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> GetByIdAsync(Guid flashSaleId, FlashSaleService flashSaleService, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var result = await flashSaleService.GetByIdAsync(flashSaleId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> ListAsync(FlashSaleService flashSaleService, CancellationToken cancellationToken, int page = 1, int pageSize = 20)
    {
        var result = await flashSaleService.ListAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> CreateAsync(CreateFlashSaleCommand command, FlashSaleService flashSaleService, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var result = await flashSaleService.CreateAsync(command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Created($"/api/commerce/flash-sales/{result.Value.Id}", result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }
}
