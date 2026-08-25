using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Learning.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// Maps every <see cref="ENROLLMENT"/> HTTP endpoint. Self-contained — creates its own top-level
/// <c>/api/learning</c> <c>MapGroup</c> (default-deny <c>.RequireAuthorization()</c>) rather than receiving
/// one from <c>LearningModule</c>, same reasoning <c>QuizEndpoints</c>'s own doc comment gives: multiple
/// entity clusters in this module are being scaffolded concurrently by different tasks/agents, so keeping
/// every cluster's routing fully self-contained means <c>LearningModule.MapLearningEndpoints</c> only ever
/// needs one line added per cluster.
/// <para>
/// Two sub-groups under <c>/api/learning</c>: bare-authenticated <c>/enrollments</c> (a learner acting on
/// their own enrollment — every handler resolves <c>IUserContext.UserId</c>, never a client-supplied id,
/// same split <c>Siri.Modules.Payout.PayoutModule.MapPayoutEndpoints</c>'s own doc comment already
/// establishes for its own <c>/instructor</c> vs <c>/admin</c> split) and
/// <see cref="AuthorizationPolicyNames.AdminOnly"/> <c>/admin/enrollments</c> (creating/listing-all/
/// revoking an enrollment is ops/administrative — see <c>EnrollmentService.CreateAsync</c>'s own doc
/// comment for why enrollment creation is admin-facing rather than a learner self-service action).
/// </para>
/// <para>
/// Endpoint bodies here are real plumbing (bind → call <see cref="EnrollmentService"/> → map
/// <see cref="Result"/> to HTTP), not part of the "business logic left as stubs" scope — only the
/// <see cref="EnrollmentService"/> method bodies they call are stubs for this scaffold pass.
/// </para>
/// </summary>
public static class EnrollmentEndpoints
{
    public static IEndpointRouteBuilder MapEnrollmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/learning").WithTags("Learning").RequireAuthorization();

        var mine = group.MapGroup("/enrollments");

        mine.MapGet("/me", ListMineAsync)
            .WithName("LearningListMyEnrollments")
            .WithSummary("รายการคอร์สที่ตัวเองลงทะเบียนไว้")
            .Produces<PagedResult<EnrollmentResponse>>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized);

        mine.MapGet("/{id:guid}", GetOwnAsync)
            .WithName("LearningGetMyEnrollment")
            .WithSummary("ดูรายละเอียดการลงทะเบียนของตัวเอง")
            .Produces<EnrollmentResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        mine.MapPut("/{id:guid}/progress", UpdateOwnProgressAsync)
            .AddEndpointFilter<ValidationEndpointFilter<UpdateEnrollmentProgressCommand>>()
            .WithName("LearningUpdateMyEnrollmentProgress")
            .WithSummary("อัปเดตความคืบหน้าการเรียนของตัวเอง")
            .Produces<EnrollmentResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        var admin = group.MapGroup("/admin/enrollments").RequireAuthorization(AuthorizationPolicyNames.AdminOnly);

        admin.MapPost("/", CreateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreateEnrollmentCommand>>()
            .WithName("LearningCreateEnrollment")
            .WithSummary("สร้างการลงทะเบียนเรียน (ops/backfill/ของขวัญ)")
            .Produces<EnrollmentResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        admin.MapGet("/", ListAsync)
            .WithName("LearningListEnrollments")
            .WithSummary("รายการการลงทะเบียนทั้งหมด (กรองตามคอร์ส/สถานะได้)")
            .Produces<PagedResult<EnrollmentResponse>>(StatusCodes.Status200OK);

        admin.MapPost("/{id:guid}/revoke", RevokeAsync)
            .WithName("LearningRevokeEnrollment")
            .WithSummary("เพิกถอนสิทธิ์การเรียน")
            .Produces<EnrollmentResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> CreateAsync(
        CreateEnrollmentCommand command,
        EnrollmentService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/learning/admin/enrollments/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> ListAsync(
        EnrollmentService service,
        CancellationToken cancellationToken,
        Guid? courseId = null,
        EnrollmentStatus? status = null,
        int page = 1,
        int pageSize = 20)
    {
        var result = await service.ListAsync(courseId, status, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> ListMineAsync(
        EnrollmentService service,
        IUserContext userContext,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.ListForUserAsync(userId, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> GetOwnAsync(
        Guid id,
        EnrollmentService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetOwnAsync(userId, id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> UpdateOwnProgressAsync(
        Guid id,
        UpdateEnrollmentProgressCommand command,
        EnrollmentService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.UpdateOwnProgressAsync(userId, id, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> RevokeAsync(
        Guid id,
        EnrollmentService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.RevokeAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }
}
