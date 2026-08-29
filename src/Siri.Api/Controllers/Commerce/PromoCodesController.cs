using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Commerce.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Commerce;

[ApiController]
[Route("api/commerce")]
[Authorize]
[Tags("Commerce")]
public class PromoCodesController : ControllerBase
{
    [HttpPost("promo-codes/validate")]
    [EndpointName("CommerceValidatePromoCode")]
    [EndpointSummary("ตรวจสอบและคำนวณส่วนลดจากโค้ดส่วนลด")]
    [ProducesResponseType(typeof(ValidatePromoCodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> Validate(
        [FromBody] ValidatePromoCodeCommand command,
        [FromServices] PromoCodeService promoCodeService,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await promoCodeService.ValidatePromoCodeAsync(userId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("admin/promo-codes")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CommerceCreatePromoCode")]
    [EndpointSummary("สร้างโค้ดส่วนลดใหม่")]
    [ProducesResponseType(typeof(PromoCodeResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> Create(
        [FromBody] CreatePromoCodeCommand command,
        [FromServices] PromoCodeService promoCodeService,
        CancellationToken cancellationToken)
    {
        var result = await promoCodeService.CreateAsync(command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Created($"/api/commerce/admin/promo-codes/{result.Value.Id}", result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("admin/promo-codes/{promoCodeId:guid}")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CommerceGetPromoCode")]
    [EndpointSummary("ดูรายละเอียดโค้ดส่วนลด")]
    [ProducesResponseType(typeof(PromoCodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetById(
        [FromRoute] Guid promoCodeId,
        [FromServices] PromoCodeService promoCodeService,
        CancellationToken cancellationToken)
    {
        var result = await promoCodeService.GetByIdAsync(promoCodeId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("admin/promo-codes")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CommerceListPromoCodes")]
    [EndpointSummary("รายการโค้ดส่วนลดทั้งหมด")]
    [ProducesResponseType(typeof(PagedResult<PromoCodeResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> List(
        [FromServices] PromoCodeService promoCodeService,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await promoCodeService.ListAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
}
