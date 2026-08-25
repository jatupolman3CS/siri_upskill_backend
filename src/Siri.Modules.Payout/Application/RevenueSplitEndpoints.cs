using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Payout.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Payout.Application;

/// <summary>
/// Maps every <c>REVENUE_SPLIT</c> HTTP endpoint — grouped into one file per entity for this module
/// (unlike Catalog's one-file-per-feature vertical slices), matching this task's Repository+Service
/// folder layout. Route groups/policies are applied by the caller (<c>PayoutModule.MapPayoutEndpoints</c>),
/// same "endpoint just maps its own relative route, the module composes the group prefix + policy" split
/// Catalog's <c>CatalogModule.MapCatalogEndpoints</c> already uses.
/// <para>
/// Every handler delegate below calls straight into <see cref="RevenueSplitService"/>, which is implemented
/// and wired into <c>Siri.Api/Program.cs</c> as of 2026-08-24 — the old scaffold note was stale. NOTE: the
/// split ratio itself is still an open decision (Q4). <c>RevenueSplitContract</c> currently hardcodes 70/30
/// with a zero payment fee and ignores <c>InstructorProfile.RevenueSharePercent</c>; that has to be
/// rewritten once Q4 is answered — see docs/TASKS.md P6-03.
/// </para>
/// </summary>
public static class RevenueSplitEndpoints
{
    /// <summary>Maps POST /api/payout/admin/revenue-splits.</summary>
    public static IEndpointRouteBuilder MapCreateRevenueSplitEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/", HandleCreateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreateRevenueSplitCommand>>()
            .WithName("PayoutCreateRevenueSplit")
            .WithSummary("บันทึกส่วนแบ่งรายได้สำหรับรายการสั่งซื้อหนึ่งรายการ")
            .Produces<RevenueSplitResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    /// <summary>Maps GET /api/payout/admin/revenue-splits/{id}.</summary>
    public static IEndpointRouteBuilder MapGetRevenueSplitEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/{id:guid}", HandleGetByIdAsync)
            .WithName("PayoutGetRevenueSplit")
            .WithSummary("ดูรายละเอียดส่วนแบ่งรายได้รายการเดียว")
            .Produces<RevenueSplitResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>Maps GET /api/payout/admin/revenue-splits?instructorId=&amp;periodKey=&amp;status=&amp;page=&amp;pageSize=.</summary>
    public static IEndpointRouteBuilder MapListRevenueSplitsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", HandleListAsync)
            .WithName("PayoutListRevenueSplits")
            .WithSummary("รายการส่วนแบ่งรายได้ (กรองตามผู้สอน/งวด/สถานะได้)")
            .Produces<PagedResult<RevenueSplitResponse>>(StatusCodes.Status200OK);

        return endpoints;
    }

    /// <summary>Maps GET /api/payout/instructor/revenue-splits?page=&amp;pageSize=.</summary>
    public static IEndpointRouteBuilder MapListMyRevenueSplitsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", HandleListMineAsync)
            .WithName("PayoutListMyRevenueSplits")
            .WithSummary("รายการส่วนแบ่งรายได้ของตัวเอง")
            .Produces<PagedResult<RevenueSplitResponse>>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(
        CreateRevenueSplitCommand command,
        RevenueSplitService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/payout/admin/revenue-splits/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleGetByIdAsync(
        Guid id,
        RevenueSplitService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleListAsync(
        RevenueSplitService service,
        CancellationToken cancellationToken,
        Guid? instructorId = null,
        string? periodKey = null,
        RevenueSplitStatus? status = null,
        int page = 1,
        int pageSize = 20)
    {
        var result = await service.ListAsync(instructorId, periodKey, status, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> HandleListMineAsync(
        RevenueSplitService service,
        IUserContext userContext,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.ListForInstructorAsync(userId, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
}
