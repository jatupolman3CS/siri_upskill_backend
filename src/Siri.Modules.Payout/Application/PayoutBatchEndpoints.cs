using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Payout.Application;

/// <summary>
/// Maps every <c>PAYOUT_BATCH</c> HTTP endpoint — same file-per-entity/group-composed-by-caller shape as
/// <see cref="RevenueSplitEndpoints"/>'s own doc comment. All three routes here are admin-only (see
/// <c>PayoutModule.MapPayoutEndpoints</c> for the policy). Every handler delegate below calls straight into
/// <see cref="PayoutBatchService"/>, which is fully stubbed for this scaffold pass — there is deliberately
/// no "execute batch" route here, see <see cref="PayoutBatchService"/>'s own doc comment for why.
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
}
