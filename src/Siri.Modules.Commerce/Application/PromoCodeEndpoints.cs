using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

/// <summary>Endpoints for <see cref="PROMO_CODE"/> — all three routes are admin-only (see
/// <c>PromoCodeService</c>'s own doc comment for why this aggregate has no public-facing routes, unlike
/// <see cref="BundleEndpoints"/>/<see cref="FlashSaleEndpoints"/>). Mapped as one group by
/// <c>CommerceModule.MapCommerceEndpoints</c> under <c>/api/commerce/admin/promo-codes</c>.</summary>
public static class PromoCodeEndpoints
{
    public static IEndpointRouteBuilder MapPromoCodeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/", CreateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreatePromoCodeCommand>>()
            .WithName("CommerceCreatePromoCode")
            .WithSummary("สร้างโค้ดส่วนลดใหม่")
            .Produces<PromoCodeResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        endpoints.MapGet("/{promoCodeId:guid}", GetByIdAsync)
            .WithName("CommerceGetPromoCode")
            .WithSummary("ดูรายละเอียดโค้ดส่วนลด")
            .Produces<PromoCodeResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        endpoints.MapGet("/", ListAsync)
            .WithName("CommerceListPromoCodes")
            .WithSummary("รายการโค้ดส่วนลดทั้งหมด")
            .Produces<PagedResult<PromoCodeResponse>>(StatusCodes.Status200OK);

        return endpoints;
    }

    public static IEndpointRouteBuilder MapCustomerPromoCodeEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/validate", ValidateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<ValidatePromoCodeCommand>>()
            .WithName("CommerceValidatePromoCode")
            .WithSummary("ตรวจสอบและคำนวณส่วนลดจากโค้ดส่วนลด")
            .Produces<ValidatePromoCodeResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ValidateAsync(
        ValidatePromoCodeCommand command,
        PromoCodeService promoCodeService,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await promoCodeService.ValidatePromoCodeAsync(userId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> CreateAsync(CreatePromoCodeCommand command, PromoCodeService promoCodeService, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var result = await promoCodeService.CreateAsync(command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Created($"/api/commerce/admin/promo-codes/{result.Value.Id}", result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> GetByIdAsync(Guid promoCodeId, PromoCodeService promoCodeService, HttpContext httpContext, CancellationToken cancellationToken)
    {
        var result = await promoCodeService.GetByIdAsync(promoCodeId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> ListAsync(PromoCodeService promoCodeService, CancellationToken cancellationToken, int page = 1, int pageSize = 20)
    {
        var result = await promoCodeService.ListAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
}
