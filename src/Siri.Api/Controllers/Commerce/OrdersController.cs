using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Commerce.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Commerce;

[ApiController]
[Route("api/commerce/orders")]
[Authorize]
[Tags("Commerce")]
public class OrdersController : ControllerBase
{
    [HttpGet("")]
    [EndpointName("CommerceListOrders")]
    [EndpointSummary("ดูประวัติคำสั่งซื้อทั้งหมดของตัวเอง")]
    [ProducesResponseType(typeof(PagedResult<OrderResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> ListMyOrders(
        [FromServices] OrderService orderService,
        [FromServices] IUserContext userContext,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await orderService.ListUserOrdersAsync(userId, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    [HttpGet("{orderId:guid}")]
    [EndpointName("CommerceGetOrder")]
    [EndpointSummary("ดูรายละเอียดคำสั่งซื้อของตัวเอง")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetById(
        [FromRoute] Guid orderId,
        [FromServices] OrderService orderService,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await orderService.GetByIdAsync(userId, orderId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("{orderId:guid}/receipt")]
    [EndpointName("CommerceGetOrderReceipt")]
    [EndpointSummary("ดาวน์โหลดใบเสร็จรับเงินสำหรับคำสั่งซื้อในรูปแบบ PDF")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileContentResult))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetReceiptPdf(
        [FromRoute] Guid orderId,
        [FromServices] TaxInvoiceService taxInvoiceService,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await taxInvoiceService.GetOrderReceiptPdfAsync(userId, orderId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.File(result.Value.Bytes, "application/pdf", result.Value.FileName)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("")]
    [EnableRateLimiting("default")]
    [EndpointName("CommerceCreateOrder")]
    [EndpointSummary("สร้างคำสั่งซื้อใหม่")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> Create(
        [FromBody] CreateOrderCommand command,
        [FromServices] OrderService orderService,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await orderService.CreateAsync(userId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Created($"/api/commerce/orders/{result.Value.Id}", result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("{orderId:guid}/cancel")]
    [EnableRateLimiting("default")]
    [EndpointName("CommerceCancelOrder")]
    [EndpointSummary("ยกเลิกคำสั่งซื้อของตัวเองที่ยังไม่ชำระเงิน")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> Cancel(
        [FromRoute] Guid orderId,
        [FromServices] OrderService orderService,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await orderService.CancelAsync(userId, orderId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }
}
