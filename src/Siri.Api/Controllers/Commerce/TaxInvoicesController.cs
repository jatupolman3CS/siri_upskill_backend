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
public class TaxInvoicesController : ControllerBase
{
    [HttpPost("tax-invoices")]
    [EndpointName("CommerceIssueTaxInvoice")]
    [EndpointSummary("ขอออกใบกำกับภาษีสำหรับคำสั่งซื้อของตัวเอง")]
    [ProducesResponseType(typeof(TaxInvoiceResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> Issue(
        [FromBody] IssueTaxInvoiceCommand command,
        [FromServices] TaxInvoiceService taxInvoiceService,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await taxInvoiceService.IssueAsync(userId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Created($"/api/commerce/tax-invoices/{result.Value.Id}", result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("tax-invoices/{taxInvoiceId:guid}")]
    [EndpointName("CommerceGetTaxInvoice")]
    [EndpointSummary("ดูใบกำกับภาษี")]
    [ProducesResponseType(typeof(TaxInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetById(
        [FromRoute] Guid taxInvoiceId,
        [FromServices] TaxInvoiceService taxInvoiceService,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await taxInvoiceService.GetByIdAsync(userId, taxInvoiceId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("tax-invoices/by-order/{orderId:guid}")]
    [EndpointName("CommerceGetTaxInvoiceByOrder")]
    [EndpointSummary("ดูใบกำกับภาษีตามรหัสคำสั่งซื้อ")]
    [ProducesResponseType(typeof(TaxInvoiceResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetByOrderId(
        [FromRoute] Guid orderId,
        [FromServices] TaxInvoiceService taxInvoiceService,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await taxInvoiceService.GetByOrderIdAsync(userId, orderId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("tax-invoices/{taxInvoiceId:guid}/pdf")]
    [EndpointName("CommerceGetTaxInvoicePdf")]
    [EndpointSummary("ดาวน์โหลดใบกำกับภาษีในรูปแบบ PDF")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileContentResult))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetPdf(
        [FromRoute] Guid taxInvoiceId,
        [FromServices] TaxInvoiceService taxInvoiceService,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await taxInvoiceService.GetPdfAsync(userId, taxInvoiceId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.File(result.Value.Bytes, "application/pdf", result.Value.FileName)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("tax-invoices/by-order/{orderId:guid}/pdf")]
    [EndpointName("CommerceGetTaxInvoicePdfByOrder")]
    [EndpointSummary("ดาวน์โหลดใบกำกับภาษีตามรหัสคำสั่งซื้อในรูปแบบ PDF")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileContentResult))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetOrderReceiptPdf(
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

    [HttpGet("admin/tax-invoices")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("CommerceListTaxInvoices")]
    [EndpointSummary("รายการใบกำกับภาษีทั้งหมด")]
    [ProducesResponseType(typeof(PagedResult<TaxInvoiceResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> List(
        [FromServices] TaxInvoiceService taxInvoiceService,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await taxInvoiceService.ListAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
}
