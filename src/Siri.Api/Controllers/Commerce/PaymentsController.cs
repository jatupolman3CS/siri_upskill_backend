using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Commerce.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Commerce;

[ApiController]
[Route("api/commerce/payments")]
[Authorize]
[Tags("Commerce")]
public class PaymentsController : ControllerBase
{
    [HttpGet("{paymentId:guid}")]
    [EndpointName("CommerceGetPayment")]
    [EndpointSummary("ดูสถานะการชำระเงิน")]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetById(
        [FromRoute] Guid paymentId,
        [FromServices] PaymentService paymentService,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await paymentService.GetByIdAsync(userId, paymentId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("")]
    [EndpointName("CommerceCreatePayment")]
    [EndpointSummary("เริ่มการชำระเงินสำหรับคำสั่งซื้อ")]
    [ProducesResponseType(typeof(PaymentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> Create(
        [FromBody] CreatePaymentCommand command,
        [FromServices] PaymentService paymentService,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await paymentService.CreateAsync(userId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Created($"/api/commerce/payments/{result.Value.Id}", result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }
}
