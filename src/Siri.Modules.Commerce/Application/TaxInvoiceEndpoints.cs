using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Commerce.Application;

/// <summary>
/// Endpoints for <see cref="TAX_INVOICE"/> — split across two mapping methods, same shape as
/// <see cref="RefundEndpoints"/>'s own doc comment: a buyer requesting/viewing their own tax invoice
/// (bare-authenticated, <see cref="MapTaxInvoiceEndpoints"/>) vs. an admin/finance-back-office-wide listing
/// (<c>AdminOnly</c>, <see cref="MapAdminTaxInvoiceEndpoints"/>).
/// </summary>
public static class TaxInvoiceEndpoints
{
    /// <summary>Maps POST /api/commerce/tax-invoices and GET /api/commerce/tax-invoices/{taxInvoiceId} —
    /// both bare-authenticated. No ownership check exists yet in <c>TaxInvoiceService.GetByIdAsync</c> (see
    /// that method's own doc comment) — this endpoint only inherits the group's default "must be logged in"
    /// policy for now, deliberately not a stronger one, same precedent <c>PaymentEndpoints.GetByIdAsync</c>'s
    /// own doc comment sets.</summary>
    public static IEndpointRouteBuilder MapTaxInvoiceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/", IssueAsync)
            .AddEndpointFilter<ValidationEndpointFilter<IssueTaxInvoiceCommand>>()
            .WithName("CommerceIssueTaxInvoice")
            .WithSummary("ขอออกใบกำกับภาษีสำหรับคำสั่งซื้อของตัวเอง")
            .Produces<TaxInvoiceResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        endpoints.MapGet("/{taxInvoiceId:guid}", GetByIdAsync)
            .WithName("CommerceGetTaxInvoice")
            .WithSummary("ดูใบกำกับภาษี")
            .Produces<TaxInvoiceResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        endpoints.MapGet("/by-order/{orderId:guid}", GetByOrderIdAsync)
            .WithName("CommerceGetTaxInvoiceByOrder")
            .WithSummary("ดูใบกำกับภาษีตามรหัสคำสั่งซื้อ")
            .Produces<TaxInvoiceResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        endpoints.MapGet("/{taxInvoiceId:guid}/pdf", GetPdfAsync)
            .WithName("CommerceGetTaxInvoicePdf")
            .WithSummary("ดาวน์โหลดใบกำกับภาษีในรูปแบบ PDF")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        endpoints.MapGet("/by-order/{orderId:guid}/pdf", GetOrderReceiptPdfAsync)
            .WithName("CommerceGetTaxInvoicePdfByOrder")
            .WithSummary("ดาวน์โหลดใบกำกับภาษีตามรหัสคำสั่งซื้อในรูปแบบ PDF")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>Maps GET /api/commerce/admin/tax-invoices?page=&amp;pageSize=.</summary>
    public static IEndpointRouteBuilder MapAdminTaxInvoiceEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", ListAsync)
            .WithName("CommerceListTaxInvoices")
            .WithSummary("รายการใบกำกับภาษีทั้งหมด")
            .Produces<PagedResult<TaxInvoiceResponse>>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task<IResult> IssueAsync(IssueTaxInvoiceCommand command, TaxInvoiceService taxInvoiceService, IUserContext userContext, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await taxInvoiceService.IssueAsync(userId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Created($"/api/commerce/tax-invoices/{result.Value.Id}", result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> GetByIdAsync(Guid taxInvoiceId, TaxInvoiceService taxInvoiceService, IUserContext userContext, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await taxInvoiceService.GetByIdAsync(userId, taxInvoiceId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> GetByOrderIdAsync(Guid orderId, TaxInvoiceService taxInvoiceService, IUserContext userContext, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await taxInvoiceService.GetByOrderIdAsync(userId, orderId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> GetPdfAsync(Guid taxInvoiceId, TaxInvoiceService taxInvoiceService, IUserContext userContext, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await taxInvoiceService.GetPdfAsync(userId, taxInvoiceId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.File(result.Value.Bytes, "application/pdf", result.Value.FileName)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> GetOrderReceiptPdfAsync(Guid orderId, TaxInvoiceService taxInvoiceService, IUserContext userContext, HttpContext httpContext, CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId) return Results.Unauthorized();

        var result = await taxInvoiceService.GetOrderReceiptPdfAsync(userId, orderId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.File(result.Value.Bytes, "application/pdf", result.Value.FileName)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> ListAsync(TaxInvoiceService taxInvoiceService, CancellationToken cancellationToken, int page = 1, int pageSize = 20)
    {
        var result = await taxInvoiceService.ListAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
}
