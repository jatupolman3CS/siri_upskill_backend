using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// Maps the instructor-authoring endpoints for <see cref="Domain.ASSIGNMENT"/>. Self-contained group —
/// see <see cref="QuizEndpoints"/>'s own doc comment for why.
/// </summary>
public static class AssignmentEndpoints
{
    public static IEndpointRouteBuilder MapAssignmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/learning/instructor/assignments")
            .WithTags("Learning")
            .RequireAuthorization(AuthorizationPolicyNames.InstructorOnly);

        group.MapPost("/", CreateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreateAssignmentRequest>>()
            .WithName("LearningCreateAssignment")
            .WithSummary("สร้างงานที่มอบหมายใหม่สำหรับบทเรียน")
            .Produces<AssignmentResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("LearningGetAssignment")
            .WithSummary("ดูรายละเอียดงานที่มอบหมาย")
            .Produces<AssignmentResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapPut("/{id:guid}", UpdateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<UpdateAssignmentRequest>>()
            .WithName("LearningUpdateAssignment")
            .WithSummary("แก้ไขงานที่มอบหมาย")
            .Produces<AssignmentResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        var learnerGroup = endpoints.MapGroup("/api/learning/assignments")
            .WithTags("Learning")
            .RequireAuthorization();

        learnerGroup.MapGet("/by-episode/{episodeId:guid}", GetByEpisodeForLearnerAsync)
            .WithName("LearningGetAssignmentByEpisode")
            .WithSummary("ดูรายละเอียดงานที่มอบหมายของบทเรียน (สำหรับผู้เรียน)")
            .Produces<AssignmentResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetByEpisodeForLearnerAsync(
        Guid episodeId,
        AssignmentService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetByEpisodeForLearnerAsync(userId, episodeId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> CreateAsync(
        CreateAssignmentRequest request,
        AssignmentService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.CreateAsync(userId, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Created($"/api/learning/instructor/assignments/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        AssignmentService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.GetByIdAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> UpdateAsync(
        Guid id,
        UpdateAssignmentRequest request,
        AssignmentService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.UpdateAsync(userId, id, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }
}
