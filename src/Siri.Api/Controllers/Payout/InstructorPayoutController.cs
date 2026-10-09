using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Siri.Api.Configuration;
using Siri.Modules.Payout.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Payout;

[ApiController]
[Route("api/payout/instructor")]
[Authorize]
[Tags("Payout Instructor")]
public class InstructorPayoutController : ControllerBase
{
    [HttpGet("revenue-splits")]
    [EndpointName("PayoutListMyRevenueSplits")]
    [EndpointSummary("รายการส่วนแบ่งรายได้ของตัวเอง")]
    [ProducesResponseType(typeof(PagedResult<RevenueSplitResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> ListMyRevenueSplits(
        [FromServices] RevenueSplitService service,
        [FromServices] IUserContext userContext,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.ListForInstructorAsync(userId, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    [HttpPost("payout-account")]
    [EndpointName("PayoutCreateInstructorPayoutAccount")]
    [EndpointSummary("ลงทะเบียนบัญชีรับเงินสำหรับผู้สอน")]
    [ProducesResponseType(typeof(InstructorPayoutAccountResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IResult> CreatePayoutAccount(
        [FromBody] CreateInstructorPayoutAccountCommand command,
        [FromServices] InstructorPayoutAccountService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.CreateForCurrentUserAsync(userId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/payout/admin/payout-accounts/{result.Value.InstructorId}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    /// <summary>
    /// Idempotent "save my bank account" for the signed-in instructor: <b>201</b> when it creates the account, <b>200</b> when it updates it (or re-confirms
    /// identical details). The account is always the caller's own — resolved from the token, never from the request. Changing any detail makes the account
    /// unverified again until an admin re-verifies it; sending the stored details unchanged keeps the verification (see
    /// <c>INSTRUCTOR_PAYOUT_ACCOUNT.UpdateDetails</c>). Instructors only (the <c>Instructor</c> role is granted on approval; admins pass the policy too but
    /// still need an instructor profile), with its own per-user write rate limit.
    /// </summary>
    [HttpPut("payout-account")]
    [Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
    [EnableRateLimiting(RateLimiterConfiguration.PayoutWritePolicyName)]
    [EndpointName("PayoutUpsertMyInstructorPayoutAccount")]
    [EndpointSummary("บันทึกบัญชีรับเงินของตัวเอง (สร้างหรืออัปเดต) — เปลี่ยนรายละเอียดแล้วต้องให้แอดมินยืนยันใหม่")]
    [ProducesResponseType(typeof(InstructorPayoutAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(InstructorPayoutAccountResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> UpsertPayoutAccount(
        [FromBody] CreateInstructorPayoutAccountCommand command,
        [FromServices] InstructorPayoutAccountService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        // An account holds the bank details of one person: never let a proxy or browser cache keep a copy of the answer.
        Response.Headers.CacheControl = "no-store";

        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.UpsertForCurrentUserAsync(userId, command, cancellationToken).ConfigureAwait(false);
        if (result.IsFailure)
        {
            return result.Error.ToProblemHttpResult(HttpContext);
        }

        return result.Value.Created
            ? Results.Created("/api/payout/instructor/payout-account", result.Value.Account)
            : Results.Ok(result.Value.Account);
    }

    [HttpGet("payout-account")]
    [EndpointName("PayoutGetMyInstructorPayoutAccount")]
    [EndpointSummary("ดูบัญชีรับเงินของตัวเอง")]
    [ProducesResponseType(typeof(InstructorPayoutAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetMyPayoutAccount(
        [FromServices] InstructorPayoutAccountService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetForCurrentUserAsync(userId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("tax-certificates/{batchItemId:guid}")]
    [EndpointName("PayoutGetTaxCertificate")]
    [EndpointSummary("ดูข้อมูลหนังสือรับรองการหักภาษี ณ ที่จ่าย (50 ทวิ)")]
    [ProducesResponseType(typeof(WithholdingTaxCertificateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetTaxCertificate(
        [FromRoute] Guid batchItemId,
        [FromServices] PayoutBatchService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var isAdmin = userContext.Roles.Contains(RoleNames.Admin) || userContext.Roles.Contains(RoleNames.SuperAdmin);
        var result = await service.GetTaxCertificateAsync(userId, batchItemId, isAdmin, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("earnings/summary")]
    [EndpointName("PayoutGetMyEarningsSummary")]
    [EndpointSummary("ดูสรุปรายได้สะสม/รอบล่าสุด/ประมาณการรอบถัดไปของผู้สอน")]
    [ProducesResponseType(typeof(InstructorEarningsSummaryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> GetEarningsSummary(
        [FromServices] InstructorEarningsService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetEarningsSummaryAsync(userId, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    [HttpGet("history")]
    [EndpointName("PayoutGetMyPayoutHistory")]
    [EndpointSummary("ดูประวัติการรับเงินโอนของผู้สอน")]
    [ProducesResponseType(typeof(PagedResult<InstructorPayoutHistoryItem>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> GetPayoutHistory(
        [FromServices] InstructorEarningsService service,
        [FromServices] IUserContext userContext,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetPayoutHistoryAsync(userId, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
}
