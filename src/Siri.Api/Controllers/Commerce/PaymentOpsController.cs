using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Commerce.Application;
using Siri.Modules.Commerce.Domain;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Commerce;

[ApiController]
[Route("api/commerce/admin/payment-ops")]
[Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
[Tags("Commerce Admin")]
public class PaymentOpsController : ControllerBase
{
    [HttpGet("")]
    [EndpointName("CommerceListPaymentOpsQueue")]
    [EndpointSummary("รายการในคิวตรวจสอบการชำระเงินที่ผิดปกติ (Admin Only)")]
    [ProducesResponseType(typeof(PagedResult<PaymentOpsQueueDto>), StatusCodes.Status200OK)]
    public async Task<IResult> List(
        [FromServices] PaymentOpsQueueService service,
        [FromQuery] PaymentOpsQueueStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await service.ListAsync(status, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    [HttpGet("{id:guid}")]
    [EndpointName("CommerceGetPaymentOpsQueueById")]
    [EndpointSummary("ดูรายละเอียดรายการในคิวตรวจสอบการชำระเงิน (Admin Only)")]
    [ProducesResponseType(typeof(PaymentOpsQueueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetById(
        [FromRoute] Guid id,
        [FromServices] PaymentOpsQueueService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("{id:guid}/assign")]
    [EndpointName("CommerceAssignPaymentOpsQueue")]
    [EndpointSummary("มอบหมายรายการให้ Admin ตรวจสอบ (Admin Only)")]
    [ProducesResponseType(typeof(PaymentOpsQueueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> Assign(
        [FromRoute] Guid id,
        [FromServices] PaymentOpsQueueService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } adminUserId) return Results.Unauthorized();

        var result = await service.AssignAsync(id, adminUserId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("{id:guid}/resolve")]
    [EndpointName("CommerceResolvePaymentOpsQueue")]
    [EndpointSummary("ดำเนินการแก้ไขปัญหาการชำระเงิน (คืนเงิน/เปิดสิทธิ์/ยกเลิก) (Admin Only)")]
    [ProducesResponseType(typeof(PaymentOpsQueueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> Resolve(
        [FromRoute] Guid id,
        [FromBody] ResolvePaymentOpsRequest request,
        [FromServices] PaymentOpsQueueService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } adminUserId) return Results.Unauthorized();

        var result = await service.ResolveAsync(id, adminUserId, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("{id:guid}/dismiss")]
    [EndpointName("CommerceDismissPaymentOpsQueue")]
    [EndpointSummary("ยกเลิก/ปิดรายการในคิวตรวจสอบการชำระเงิน (Admin Only)")]
    [ProducesResponseType(typeof(PaymentOpsQueueDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> Dismiss(
        [FromRoute] Guid id,
        [FromBody] DismissPaymentOpsRequest request,
        [FromServices] PaymentOpsQueueService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } adminUserId) return Results.Unauthorized();

        var result = await service.DismissAsync(id, adminUserId, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }
}
