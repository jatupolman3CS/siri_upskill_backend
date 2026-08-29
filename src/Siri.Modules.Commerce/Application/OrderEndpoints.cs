using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

public static class OrderEndpoints
{
    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", ListMyOrdersAsync)
            .WithName("CommerceListOrders")
            .WithSummary("ดูประวัติคำสั่งซื้อทั้งหมดของตัวเอง")
            .Produces<PagedResult<OrderResponse>>(StatusCodes.Status200OK);

        endpoints.MapGet("/{orderId:guid}", GetByIdAsync)
            .WithName("CommerceGetOrder")
            .WithSummary("ดูรายละเอียดคำสั่งซื้อของตัวเอง")
            .Produces<OrderResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        endpoints.MapGet("/{orderId:guid}/receipt", GetReceiptPdfAsync)
            .WithName("CommerceGetOrderReceipt")
            .WithSummary("ดาวน์โหลดใบเสร็จรับเงินสำหรับคำสั่งซื้อในรูปแบบ PDF")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        // Route metadata (.WithName/.Produces) is what /openapi/v1.json is built from —
        // AddOpenApi()/MapOpenApi() read it at map-time and never invoke the handler, so the published
        // contract stays accurate independently of the handler body.
        endpoints.MapPost("/", CreateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreateOrderCommand>>()
            .WithName("CommerceCreateOrder")
            .WithSummary("สร้างคำสั่งซื้อใหม่")
            .Produces<OrderResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        return endpoints;
    }

    private static async Task<IResult> GetReceiptPdfAsync(Guid orderId, TaxInvoiceService taxInvoiceService, IUserContext userContext, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await taxInvoiceService.GetOrderReceiptPdfAsync(userId, orderId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.File(result.Value.Bytes, "application/pdf", result.Value.FileName)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> ListMyOrdersAsync(OrderService orderService, IUserContext userContext, CancellationToken cancellationToken, int page = 1, int pageSize = 20)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await orderService.ListUserOrdersAsync(userId, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetByIdAsync(Guid orderId, OrderService orderService, IUserContext userContext, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await orderService.GetByIdAsync(userId, orderId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> CreateAsync(CreateOrderCommand command, OrderService orderService, IUserContext userContext, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await orderService.CreateAsync(userId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Created($"/api/commerce/orders/{result.Value.Id}", result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }
}
