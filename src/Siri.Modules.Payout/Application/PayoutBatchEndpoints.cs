using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Payout.Application;

/// <summary>
/// Maps every <c>PAYOUT_BATCH</c> HTTP endpoint.
/// </summary>
public static class PayoutBatchEndpoints
{
    /// <summary>Maps POST /api/payout/admin/batches.</summary>
    public static IEndpointRouteBuilder MapCreatePayoutBatchEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/", HandleCreateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreatePayoutBatchCommand>>()
            .WithName("PayoutCreateBatch")
            .WithSummary("เปิดรอบจ่ายเงินใหม่สำหรับงวดที่ระบุ")
            .Produces<PayoutBatchResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        return endpoints;
    }

    /// <summary>Maps GET /api/payout/admin/batches/{id}.</summary>
    public static IEndpointRouteBuilder MapGetPayoutBatchEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/{id:guid}", HandleGetByIdAsync)
            .WithName("PayoutGetBatch")
            .WithSummary("ดูรายละเอียดรอบจ่ายเงินพร้อมรายการต่อผู้สอน")
            .Produces<PayoutBatchResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>Maps GET /api/payout/admin/batches?page=&amp;pageSize=.</summary>
    public static IEndpointRouteBuilder MapListPayoutBatchesEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", HandleListAsync)
            .WithName("PayoutListBatches")
            .WithSummary("รายการรอบจ่ายเงินทั้งหมด")
            .Produces<PagedResult<PayoutBatchResponse>>(StatusCodes.Status200OK);

        return endpoints;
    }

    /// <summary>Maps POST /api/payout/admin/batches/{id}/execute.</summary>
    public static IEndpointRouteBuilder MapExecutePayoutBatchEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/{id:guid}/execute", HandleExecuteAsync)
            .WithName("PayoutExecuteBatch")
            .WithSummary("ยืนยันและประมวลผลการจ่ายเงินของรอบนี้ (แอดมิน)")
            .Produces<PayoutBatchResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status400BadRequest)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    /// <summary>Maps GET /api/payout/admin/batches/{id}/export.</summary>
    public static IEndpointRouteBuilder MapExportPayoutBatchEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/{id:guid}/export", HandleExportAsync)
            .WithName("PayoutExportBatch")
            .WithSummary("ส่งออกไฟล์ข้อมูลโอนเงินธนาคารสำหรับรอบนี้ (แอดมิน)")
            .Produces<BatchExportResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>Maps GET /api/payout/instructor/tax-certificates/{batchItemId}.</summary>
    public static IEndpointRouteBuilder MapGetTaxCertificateEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/tax-certificates/{batchItemId:guid}", HandleGetTaxCertificateAsync)
            .WithName("PayoutGetTaxCertificate")
            .WithSummary("ดูข้อมูลหนังสือรับรองการหักภาษี ณ ที่จ่าย (50 ทวิ)")
            .Produces<WithholdingTaxCertificateResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(
        CreatePayoutBatchCommand command,
        PayoutBatchService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/payout/admin/batches/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleGetByIdAsync(
        Guid id,
        PayoutBatchService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleListAsync(
        PayoutBatchService service,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20)
    {
        var result = await service.ListAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> HandleExecuteAsync(
        Guid id,
        PayoutBatchService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var adminId = userContext.UserId ?? Guid.Empty;
        var result = await service.ExecuteBatchAsync(id, adminId, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleExportAsync(
        Guid id,
        PayoutBatchService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.ExportBatchTransferFileAsync(id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleGetTaxCertificateAsync(
        Guid batchItemId,
        PayoutBatchService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var isAdmin = userContext.Roles.Contains(RoleNames.Admin) || userContext.Roles.Contains(RoleNames.SuperAdmin);
        var result = await service.GetTaxCertificateAsync(userId, batchItemId, isAdmin, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }
}
