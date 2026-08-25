using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Community.Application.Response;
using Siri.SharedKernel;

namespace Siri.Modules.Community.Application;

/// <summary>
/// Maps every <c>REPORT</c> HTTP endpoint — same file-per-entity/group-composed-by-caller shape as
/// <see cref="DiscussionEndpoints"/>'s own doc comment. <see cref="MapListPendingReportsEndpoint"/>,
/// <see cref="MapResolveReportEndpoint"/>, and <see cref="MapDismissReportEndpoint"/> are moderation
/// actions — <c>CommunityModule.MapCommunityEndpoints</c> is responsible for putting them behind
/// <c>AuthorizationPolicyNames.AdminOnly</c>, not this file (endpoints stay policy-agnostic, same split
/// Catalog's admin sub-groups already use).
/// </summary>
public static class ReportEndpoints
{
    /// <summary>Maps POST /api/community/reports.</summary>
    public static IEndpointRouteBuilder MapCreateReportEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/", HandleCreateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreateReportCommand>>()
            .WithName("CommunityCreateReport")
            .WithSummary("รายงานกระทู้ที่ไม่เหมาะสม")
            .Produces<ReportResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>Maps GET /api/community/reports/pending?page=&amp;pageSize= — admin moderation queue.</summary>
    public static IEndpointRouteBuilder MapListPendingReportsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/pending", HandleListPendingAsync)
            .WithName("CommunityListPendingReports")
            .WithSummary("รายการรายงานที่รอตรวจสอบ")
            .Produces<PagedResult<ReportResponse>>(StatusCodes.Status200OK);

        return endpoints;
    }

    /// <summary>Maps POST /api/community/reports/{id}/resolve — admin only.</summary>
    public static IEndpointRouteBuilder MapResolveReportEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/{id:guid}/resolve", HandleResolveAsync)
            .WithName("CommunityResolveReport")
            .WithSummary("ทำเครื่องหมายว่ารายงานนี้ตรวจสอบแล้ว")
            .Produces<ReportResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>Maps POST /api/community/reports/{id}/dismiss — admin only.</summary>
    public static IEndpointRouteBuilder MapDismissReportEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/{id:guid}/dismiss", HandleDismissAsync)
            .WithName("CommunityDismissReport")
            .WithSummary("ปิดรายงานโดยไม่พบปัญหา")
            .Produces<ReportResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(
        CreateReportCommand command,
        ReportService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.CreateAsync(userId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/community/reports/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleListPendingAsync(
        ReportService service,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = ReportService.DefaultPageSize)
    {
        var result = await service.ListPendingAsync(page, pageSize, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleResolveAsync(
        Guid id,
        ReportService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.ResolveAsync(id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleDismissAsync(
        Guid id,
        ReportService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.DismissAsync(id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }
}
