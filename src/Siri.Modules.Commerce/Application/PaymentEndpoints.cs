using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

public static class PaymentEndpoints
{
    public static IEndpointRouteBuilder MapPaymentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Ownership IS enforced as of 2026-08-24 (the older "no ownership check yet" note was stale):
        // PaymentService.GetByIdAsync resolves the payment's ORDER_ID and returns the same NotFound error
        // when order.USER_ID != caller, so another user's payment id is indistinguishable from one that
        // does not exist. Group default-deny + that check IS the access rule — do not weaken either.
        endpoints.MapGet("/config", GetConfigAsync)
            .WithName("CommerceGetPaymentConfig")
            .WithSummary("ดึง publishable key และวิธีชำระเงินที่เปิดใช้งาน")
            .Produces<PaymentConfigResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status503ServiceUnavailable);

        endpoints.MapGet("/{paymentId:guid}", GetByIdAsync)
            .WithName("CommerceGetPayment")
            .WithSummary("ดูสถานะการชำระเงิน")
            .Produces<PaymentResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        endpoints.MapPost("/", CreateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreatePaymentCommand>>()
            .WithName("CommerceCreatePayment")
            .WithSummary("เริ่มการชำระเงินสำหรับคำสั่งซื้อ")
            .Produces<PaymentResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static IResult GetConfigAsync(PaymentService paymentService, HttpContext httpContext)
    {
        var result = paymentService.GetConfig();
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> GetByIdAsync(Guid paymentId, PaymentService paymentService, IUserContext userContext, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await paymentService.GetByIdAsync(userId, paymentId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> CreateAsync(CreatePaymentCommand command, PaymentService paymentService, IUserContext userContext, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await paymentService.CreateAsync(userId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Created($"/api/commerce/payments/{result.Value.Id}", result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }
}
