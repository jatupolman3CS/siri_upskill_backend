using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Payout.Application;

/// <summary>
/// Maps every <c>INSTRUCTOR_PAYOUT_ACCOUNT</c> HTTP endpoint — same file-per-entity/group-composed-by-caller
/// shape as <see cref="RevenueSplitEndpoints"/>'s own doc comment. Every handler delegate below calls
/// straight into <see cref="InstructorPayoutAccountService"/>, which is fully stubbed for this scaffold
/// pass.
/// </summary>
public static class InstructorPayoutAccountEndpoints
{
    /// <summary>Maps POST /api/payout/instructor/payout-account.</summary>
    public static IEndpointRouteBuilder MapCreateInstructorPayoutAccountEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/", HandleCreateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreateInstructorPayoutAccountCommand>>()
            .WithName("PayoutCreateInstructorPayoutAccount")
            .WithSummary("ลงทะเบียนบัญชีรับเงินสำหรับผู้สอน")
            .Produces<InstructorPayoutAccountResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    /// <summary>Maps GET /api/payout/instructor/payout-account.</summary>
    public static IEndpointRouteBuilder MapGetMyInstructorPayoutAccountEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", HandleGetForCurrentUserAsync)
            .WithName("PayoutGetMyInstructorPayoutAccount")
            .WithSummary("ดูบัญชีรับเงินของตัวเอง")
            .Produces<InstructorPayoutAccountResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>Maps GET /api/payout/admin/payout-accounts/{instructorId}.</summary>
    public static IEndpointRouteBuilder MapGetInstructorPayoutAccountByInstructorIdEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/{instructorId:guid}", HandleGetByInstructorIdAsync)
            .WithName("PayoutGetInstructorPayoutAccountByInstructorId")
            .WithSummary("ดูบัญชีรับเงินของผู้สอนรายใดรายหนึ่ง (แอดมิน)")
            .Produces<InstructorPayoutAccountResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>Maps GET /api/payout/admin/payout-accounts?page=&amp;pageSize=.</summary>
    public static IEndpointRouteBuilder MapListInstructorPayoutAccountsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", HandleListAsync)
            .WithName("PayoutListInstructorPayoutAccounts")
            .WithSummary("รายการบัญชีรับเงินของผู้สอนทั้งหมด (แอดมิน)")
            .Produces<PagedResult<InstructorPayoutAccountResponse>>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(
        CreateInstructorPayoutAccountCommand command,
        InstructorPayoutAccountService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.CreateForCurrentUserAsync(userId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/payout/admin/payout-accounts/{result.Value.InstructorId}", result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleGetForCurrentUserAsync(
        InstructorPayoutAccountService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetForCurrentUserAsync(userId, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleGetByInstructorIdAsync(
        Guid instructorId,
        InstructorPayoutAccountService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.GetByInstructorIdAsync(instructorId, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleListAsync(
        InstructorPayoutAccountService service,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20)
    {
        var result = await service.ListAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
}
