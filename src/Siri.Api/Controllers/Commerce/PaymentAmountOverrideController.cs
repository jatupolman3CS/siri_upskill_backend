using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Siri.Modules.Commerce.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Commerce;

/// <summary>
/// Admin-only switch for the payment amount override (see <c>PAYMENT_AMOUNT_OVERRIDE</c>): while on, new
/// PaymentIntents are created for a fixed THB amount instead of the order total. Affects real money, so every
/// change is an append-only audit row attributed to the acting admin (taken from the token via
/// <see cref="IUserContext"/>, never from the request body).
/// </summary>
[ApiController]
[Route("api/commerce/admin/payment-amount-override")]
[Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
[Tags("Commerce Admin")]
public class PaymentAmountOverrideController : ControllerBase
{
    [HttpGet("")]
    [EndpointName("CommerceGetPaymentAmountOverride")]
    [EndpointSummary("ดูการตั้งค่า override ยอดเงินที่ส่งให้ Stripe ที่ใช้อยู่ตอนนี้ (Admin Only)")]
    [ProducesResponseType(typeof(PaymentAmountOverrideState), StatusCodes.Status200OK)]
    public async Task<IResult> Get(
        [FromServices] PaymentAmountOverrideService service,
        CancellationToken cancellationToken)
    {
        var state = await service.GetCurrentAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(state);
    }

    [HttpPut("")]
    [EndpointName("CommerceSetPaymentAmountOverride")]
    [EndpointSummary("เปิด/ปิด/เปลี่ยนยอด override ของเงินที่ส่งให้ Stripe (Admin Only, บันทึก audit)")]
    [ProducesResponseType(typeof(PaymentAmountOverrideState), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> Set(
        [FromBody] SetPaymentAmountOverrideCommand command,
        [FromServices] PaymentAmountOverrideService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } adminUserId) return Results.Unauthorized();

        var result = await service.SetAsync(adminUserId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("history")]
    [EndpointName("CommerceListPaymentAmountOverrideHistory")]
    [EndpointSummary("ประวัติการเปลี่ยนการตั้งค่า override ยอดเงิน เรียงใหม่สุดก่อน (Admin Only)")]
    [ProducesResponseType(typeof(PagedResult<PaymentAmountOverrideEntry>), StatusCodes.Status200OK)]
    public async Task<IResult> History(
        [FromServices] PaymentAmountOverrideService service,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await service.ListHistoryAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
}
