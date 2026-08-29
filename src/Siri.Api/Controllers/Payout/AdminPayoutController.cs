using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Payout.Application;
using Siri.Modules.Payout.Domain;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Payout;

[ApiController]
[Route("api/payout/admin")]
[Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
[Tags("Payout Admin")]
public class AdminPayoutController : ControllerBase
{
    [HttpPost("revenue-splits")]
    [EndpointName("PayoutCreateRevenueSplit")]
    [EndpointSummary("บันทึกส่วนแบ่งรายได้สำหรับรายการสั่งซื้อหนึ่งรายการ")]
    [ProducesResponseType(typeof(RevenueSplitResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> CreateRevenueSplit(
        [FromBody] CreateRevenueSplitCommand command,
        [FromServices] RevenueSplitService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/payout/admin/revenue-splits/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("revenue-splits/{id:guid}")]
    [EndpointName("PayoutGetRevenueSplit")]
    [EndpointSummary("ดูรายละเอียดส่วนแบ่งรายได้รายการเดียว")]
    [ProducesResponseType(typeof(RevenueSplitResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetRevenueSplit(
        [FromRoute] Guid id,
        [FromServices] RevenueSplitService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("revenue-splits")]
    [EndpointName("PayoutListRevenueSplits")]
    [EndpointSummary("รายการส่วนแบ่งรายได้ (กรองตามผู้สอน/งวด/สถานะได้)")]
    [ProducesResponseType(typeof(PagedResult<RevenueSplitResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> ListRevenueSplits(
        [FromServices] RevenueSplitService service,
        [FromQuery] Guid? instructorId = null,
        [FromQuery] string? periodKey = null,
        [FromQuery] RevenueSplitStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await service.ListAsync(instructorId, periodKey, status, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    [HttpGet("payout-accounts/{instructorId:guid}")]
    [EndpointName("PayoutGetInstructorPayoutAccountByInstructorId")]
    [EndpointSummary("ดูบัญชีรับเงินของผู้สอนรายใดรายหนึ่ง (แอดมิน)")]
    [ProducesResponseType(typeof(InstructorPayoutAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetPayoutAccountByInstructorId(
        [FromRoute] Guid instructorId,
        [FromServices] InstructorPayoutAccountService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetByInstructorIdAsync(instructorId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("payout-accounts/{instructorId:guid}/verify")]
    [EndpointName("PayoutVerifyInstructorPayoutAccount")]
    [EndpointSummary("ยืนยันความถูกต้องของบัญชีรับเงินผู้สอน (แอดมิน)")]
    [ProducesResponseType(typeof(InstructorPayoutAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> VerifyPayoutAccount(
        [FromRoute] Guid instructorId,
        [FromServices] InstructorPayoutAccountService service,
        CancellationToken cancellationToken)
    {
        var result = await service.VerifyAsync(instructorId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("payout-accounts")]
    [EndpointName("PayoutListInstructorPayoutAccounts")]
    [EndpointSummary("รายการบัญชีรับเงินของผู้สอนทั้งหมด (แอดมิน)")]
    [ProducesResponseType(typeof(PagedResult<InstructorPayoutAccountResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> ListPayoutAccounts(
        [FromServices] InstructorPayoutAccountService service,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await service.ListAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    [HttpPost("batches")]
    [EndpointName("PayoutCreateBatch")]
    [EndpointSummary("เปิดรอบจ่ายเงินใหม่สำหรับงวดที่ระบุ")]
    [ProducesResponseType(typeof(PayoutBatchResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> CreateBatch(
        [FromBody] CreatePayoutBatchCommand command,
        [FromServices] PayoutBatchService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/payout/admin/batches/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("batches/{id:guid}")]
    [EndpointName("PayoutGetBatch")]
    [EndpointSummary("ดูรายละเอียดรอบจ่ายเงินพร้อมรายการต่อผู้สอน")]
    [ProducesResponseType(typeof(PayoutBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetBatch(
        [FromRoute] Guid id,
        [FromServices] PayoutBatchService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("batches")]
    [EndpointName("PayoutListBatches")]
    [EndpointSummary("รายการรอบจ่ายเงินทั้งหมด")]
    [ProducesResponseType(typeof(PagedResult<PayoutBatchResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> ListBatches(
        [FromServices] PayoutBatchService service,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await service.ListAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    [HttpPost("batches/{id:guid}/execute")]
    [EndpointName("PayoutExecuteBatch")]
    [EndpointSummary("ยืนยันและประมวลผลการจ่ายเงินของรอบนี้ (แอดมิน)")]
    [ProducesResponseType(typeof(PayoutBatchResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> ExecuteBatch(
        [FromRoute] Guid id,
        [FromServices] PayoutBatchService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var adminId = userContext.UserId ?? Guid.Empty;
        var result = await service.ExecuteBatchAsync(id, adminId, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("batches/{id:guid}/export")]
    [EndpointName("PayoutExportBatch")]
    [EndpointSummary("ส่งออกไฟล์ข้อมูลโอนเงินธนาคารสำหรับรอบนี้ (แอดมิน)")]
    [ProducesResponseType(typeof(BatchExportResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> ExportBatch(
        [FromRoute] Guid id,
        [FromServices] PayoutBatchService service,
        CancellationToken cancellationToken)
    {
        var result = await service.ExportBatchTransferFileAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }
}
