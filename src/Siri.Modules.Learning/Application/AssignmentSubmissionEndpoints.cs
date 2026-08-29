using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// Maps the endpoints for <see cref="Domain.ASSIGNMENT_SUBMISSION"/> — two self-contained groups (see
/// <see cref="QuizEndpoints"/>'s own doc comment for why every cluster is self-contained): the
/// learner-facing submit/view surface (default-authenticated, same "role gate is not ownership" shape
/// <see cref="QuizAttemptEndpoints"/> already uses) and the instructor-facing review/grade surface
/// (<see cref="AuthorizationPolicyNames.InstructorOnly"/>).
/// </summary>
public static class AssignmentSubmissionEndpoints
{
    public static IEndpointRouteBuilder MapAssignmentSubmissionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var learnerGroup = endpoints.MapGroup("/api/learning/assignment-submissions")
            .WithTags("Learning")
            .RequireAuthorization();

        learnerGroup.MapPost("/", SubmitAsync)
            .AddEndpointFilter<ValidationEndpointFilter<SubmitAssignmentRequest>>()
            .WithName("LearningSubmitAssignment")
            .WithSummary("ส่งงานที่มอบหมาย")
            .Produces<AssignmentSubmissionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        learnerGroup.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("LearningGetAssignmentSubmission")
            .WithSummary("ดูสถานะงานที่ส่ง")
            .Produces<AssignmentSubmissionResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        learnerGroup.MapGet("/by-assignment/{assignmentId:guid}", GetMySubmissionByAssignmentAsync)
            .WithName("LearningGetMyAssignmentSubmissionByAssignment")
            .WithSummary("ดูงานที่ส่งล่าสุดของตัวเองในงานที่มอบหมายนี้")
            .Produces<AssignmentSubmissionResponse>(StatusCodes.Status200OK)
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        var instructorGroup = endpoints.MapGroup("/api/learning/instructor/assignment-submissions")
            .WithTags("Learning")
            .RequireAuthorization(AuthorizationPolicyNames.InstructorOnly);

        instructorGroup.MapGet("/by-assignment/{assignmentId:guid}", ListByAssignmentAsync)
            .WithName("LearningListAssignmentSubmissions")
            .WithSummary("รายการงานที่ส่งเข้ามาของงานที่มอบหมายหนึ่งชิ้น")
            .Produces<PagedResult<AssignmentSubmissionResponse>>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        instructorGroup.MapPost("/{id:guid}/grade", GradeAsync)
            .AddEndpointFilter<ValidationEndpointFilter<GradeAssignmentSubmissionRequest>>()
            .WithName("LearningGradeAssignmentSubmission")
            .WithSummary("ให้คะแนน/ตีกลับงานที่ส่ง")
            .Produces<AssignmentSubmissionResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> GetMySubmissionByAssignmentAsync(
        Guid assignmentId,
        AssignmentSubmissionService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetMySubmissionByAssignmentAsync(userId, assignmentId, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return result.Error.ToProblemHttpResult(httpContext);
        }

        return result.Value is not null ? Results.Ok(result.Value) : Results.NoContent();
    }

    private static async Task<IResult> SubmitAsync(
        SubmitAssignmentRequest request,
        AssignmentSubmissionService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.SubmitAsync(userId, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Created($"/api/learning/assignment-submissions/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        AssignmentSubmissionService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetByIdAsync(userId, id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> ListByAssignmentAsync(
        Guid assignmentId,
        AssignmentSubmissionService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = AssignmentSubmissionService.DefaultPageSize)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.ListByAssignmentAsync(userId, assignmentId, page, pageSize, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> GradeAsync(
        Guid id,
        GradeAssignmentSubmissionRequest request,
        AssignmentSubmissionService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GradeAsync(userId, id, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }
}
