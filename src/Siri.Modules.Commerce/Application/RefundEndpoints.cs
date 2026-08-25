using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

/// <summary>
/// Endpoints for <see cref="REFUND"/> — split across two mapping methods because this aggregate's
/// workflow spans two policy tiers: a buyer requesting/viewing their own refund (bare-authenticated,
/// mapped here by <see cref="MapRefundEndpoints"/>) vs. an admin deciding it (<c>AdminOnly</c>, mapped by
/// <see cref="MapAdminRefundEndpoints"/>) — same split <c>CommerceModule.MapCommerceEndpoints</c> mirrors
/// from <c>PayoutModule.MapPayoutEndpoints</c>'s instructor-vs-admin sub-group pattern.
/// </summary>
public static class RefundEndpoints
{
    /// <summary>Maps POST /api/commerce/refunds and GET /api/commerce/refunds/{refundId} — both
    /// bare-authenticated, scoped to the caller's own refund requests.</summary>
    public static IEndpointRouteBuilder MapRefundEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/", RequestAsync)
            .AddEndpointFilter<ValidationEndpointFilter<RequestRefundCommand>>()
            .WithName("CommerceRequestRefund")
            .WithSummary("ขอคืนเงินสำหรับการชำระเงินของตัวเอง")
            .Produces<RefundResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        endpoints.MapGet("/{refundId:guid}", GetByIdAsync)
            .WithName("CommerceGetRefund")
            .WithSummary("ดูสถานะคำขอคืนเงินของตัวเอง")
            .Produces<RefundResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>Maps the admin decision workflow: GET /api/commerce/admin/refunds/pending, POST
    /// /api/commerce/admin/refunds/{refundId}/approve, POST /api/commerce/admin/refunds/{refundId}/reject —
    /// backs the finance-report mockup's approve/reject buttons.</summary>
    public static IEndpointRouteBuilder MapAdminRefundEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/pending", ListPendingAsync)
            .WithName("CommerceListPendingRefunds")
            .WithSummary("รายการคำขอคืนเงินที่รอการพิจารณา")
            .Produces<PagedResult<RefundResponse>>(StatusCodes.Status200OK);

        endpoints.MapPost("/{refundId:guid}/approve", ApproveAsync)
            .AddEndpointFilter<ValidationEndpointFilter<ApproveRefundCommand>>()
            .WithName("CommerceApproveRefund")
            .WithSummary("อนุมัติคำขอคืนเงิน")
            .Produces<RefundResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        endpoints.MapPost("/{refundId:guid}/reject", RejectAsync)
            .AddEndpointFilter<ValidationEndpointFilter<RejectRefundCommand>>()
            .WithName("CommerceRejectRefund")
            .WithSummary("ปฏิเสธคำขอคืนเงิน")
            .Produces<RefundResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> RequestAsync(RequestRefundCommand command, RefundService refundService, IUserContext userContext, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await refundService.RequestAsync(userId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Created($"/api/commerce/refunds/{result.Value.Id}", result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> GetByIdAsync(Guid refundId, RefundService refundService, IUserContext userContext, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await refundService.GetByIdAsync(userId, refundId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> ListPendingAsync(RefundService refundService, CancellationToken cancellationToken, int page = 1, int pageSize = 20)
    {
        var result = await refundService.ListPendingAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> ApproveAsync(Guid refundId, ApproveRefundCommand command, RefundService refundService, IUserContext userContext, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } decidedByUserId) return Results.Unauthorized();

        var result = await refundService.ApproveAsync(decidedByUserId, refundId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> RejectAsync(Guid refundId, RejectRefundCommand command, RefundService refundService, IUserContext userContext, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } decidedByUserId) return Results.Unauthorized();

        var result = await refundService.RejectAsync(decidedByUserId, refundId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }
}
