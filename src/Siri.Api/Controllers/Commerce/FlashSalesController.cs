using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Commerce.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Commerce;

[ApiController]
[Route("api/commerce")]
[Tags("Commerce")]
public class FlashSalesController : ControllerBase
{
    [HttpGet("flash-sales/{flashSaleId:guid}")]
    [AllowAnonymous]
    [EndpointName("CommerceGetFlashSale")]
    [EndpointSummary("ดูรายละเอียดแฟลชเซล")]
    [ProducesResponseType(typeof(FlashSaleResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetById(
        [FromRoute] Guid flashSaleId,
        [FromServices] FlashSaleService flashSaleService,
        CancellationToken cancellationToken)
    {
        var result = await flashSaleService.GetByIdAsync(flashSaleId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("flash-sales")]
    [AllowAnonymous]
    [EndpointName("CommerceListFlashSales")]
    [EndpointSummary("รายการแฟลชเซลทั้งหมด")]
    [ProducesResponseType(typeof(PagedResult<FlashSaleResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> List(
        [FromServices] FlashSaleService flashSaleService,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await flashSaleService.ListAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    [HttpPost("admin/flash-sales")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CommerceCreateFlashSale")]
    [EndpointSummary("สร้างแฟลชเซลใหม่")]
    [ProducesResponseType(typeof(FlashSaleResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> Create(
        [FromBody] CreateFlashSaleCommand command,
        [FromServices] FlashSaleService flashSaleService,
        CancellationToken cancellationToken)
    {
        var result = await flashSaleService.CreateAsync(command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Created($"/api/commerce/flash-sales/{result.Value.Id}", result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }
}
