using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Commerce.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

public static class PaymentOpsEndpoints
{
    public static IEndpointRouteBuilder MapPaymentOpsEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", ListAsync)
            .WithName("CommerceListPaymentOpsQueue")
            .WithSummary("รายการในคิวตรวจสอบการชำระเงินที่ผิดปกติ (Admin Only)")
            .Produces<PagedResult<PaymentOpsQueueDto>>(StatusCodes.Status200OK);

        endpoints.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("CommerceGetPaymentOpsQueueById")
            .WithSummary("ดูรายละเอียดรายการในคิวตรวจสอบการชำระเงิน (Admin Only)")
            .Produces<PaymentOpsQueueDto>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        endpoints.MapPost("/{id:guid}/assign", AssignAsync)
            .WithName("CommerceAssignPaymentOpsQueue")
            .WithSummary("มอบหมายรายการให้ Admin ตรวจสอบ (Admin Only)")
            .Produces<PaymentOpsQueueDto>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        endpoints.MapPost("/{id:guid}/resolve", ResolveAsync)
            .AddEndpointFilter<ValidationEndpointFilter<ResolvePaymentOpsRequest>>()
            .WithName("CommerceResolvePaymentOpsQueue")
            .WithSummary("ดำเนินการแก้ไขปัญหาการชำระเงิน (คืนเงิน/เปิดสิทธิ์/ยกเลิก) (Admin Only)")
            .Produces<PaymentOpsQueueDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        endpoints.MapPost("/{id:guid}/dismiss", DismissAsync)
            .AddEndpointFilter<ValidationEndpointFilter<DismissPaymentOpsRequest>>()
            .WithName("CommerceDismissPaymentOpsQueue")
            .WithSummary("ยกเลิก/ปิดรายการในคิวตรวจสอบการชำระเงิน (Admin Only)")
            .Produces<PaymentOpsQueueDto>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> ListAsync(
        PaymentOpsQueueService service,
        CancellationToken cancellationToken,
        [FromQuery] PaymentOpsQueueStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        var result = await service.ListAsync(status, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        PaymentOpsQueueService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> AssignAsync(
        Guid id,
        PaymentOpsQueueService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } adminUserId) return Results.Unauthorized();

        var result = await service.AssignAsync(id, adminUserId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> ResolveAsync(
        Guid id,
        ResolvePaymentOpsRequest request,
        PaymentOpsQueueService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } adminUserId) return Results.Unauthorized();

        var result = await service.ResolveAsync(id, adminUserId, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> DismissAsync(
        Guid id,
        DismissPaymentOpsRequest request,
        PaymentOpsQueueService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } adminUserId) return Results.Unauthorized();

        var result = await service.DismissAsync(id, adminUserId, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }
}
