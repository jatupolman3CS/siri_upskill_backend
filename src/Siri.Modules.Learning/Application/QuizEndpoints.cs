using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// Maps the instructor-authoring endpoints for <see cref="Domain.QUIZ"/>. Self-contained — creates its
/// own <c>MapGroup</c> (route prefix + <see cref="AuthorizationPolicyNames.InstructorOnly"/> policy)
/// rather than receiving one from <c>LearningModule</c>, unlike Catalog's per-feature endpoint files
/// (which share group builders <c>CatalogModule.MapCatalogEndpoints</c> creates centrally). Deliberate
/// for this module: <c>LearningModule.cs</c> is being edited concurrently by another task/agent
/// (Enrollment/Progress/Certificate) — keeping every entity cluster's routing fully self-contained
/// means <c>LearningModule.MapLearningEndpoints</c> only ever needs one line added per cluster
/// (<c>endpoints.MapQuizEndpoints();</c>), minimizing collision risk in that shared file.
/// <para>
/// Endpoint bodies here are real plumbing (bind → call <see cref="QuizService"/> → map <see cref="Result"/>
/// to HTTP), not part of the "business logic left as stubs" scope — only the <see cref="QuizService"/>
/// method bodies they call are stubs for this scaffold pass.
/// </para>
/// </summary>
public static class QuizEndpoints
{
    public static IEndpointRouteBuilder MapQuizEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/learning/instructor/quizzes")
            .WithTags("Learning")
            .RequireAuthorization(AuthorizationPolicyNames.InstructorOnly);

        group.MapPost("/", CreateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreateQuizRequest>>()
            .WithName("LearningCreateQuiz")
            .WithSummary("สร้างแบบทดสอบใหม่สำหรับบทเรียน")
            .Produces<QuizResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("LearningGetQuiz")
            .WithSummary("ดูแบบทดสอบพร้อมคำถาม/ตัวเลือกทั้งหมด")
            .Produces<QuizResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/questions", AddQuestionAsync)
            .AddEndpointFilter<ValidationEndpointFilter<AddQuizQuestionRequest>>()
            .WithName("LearningAddQuizQuestion")
            .WithSummary("เพิ่มคำถามในแบบทดสอบ")
            .Produces<QuizQuestionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapPost("/{quizId:guid}/questions/{questionId:guid}/options", AddOptionAsync)
            .AddEndpointFilter<ValidationEndpointFilter<AddQuizOptionRequest>>()
            .WithName("LearningAddQuizOption")
            .WithSummary("เพิ่มตัวเลือกคำตอบในคำถาม")
            .Produces<QuizOptionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/activate", ActivateAsync)
            .WithName("LearningActivateQuiz")
            .WithSummary("เปิดใช้งานแบบทดสอบ")
            .Produces<QuizResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/deactivate", DeactivateAsync)
            .WithName("LearningDeactivateQuiz")
            .WithSummary("ปิดใช้งานแบบทดสอบ")
            .Produces<QuizResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        var learnerGroup = endpoints.MapGroup("/api/learning/quizzes")
            .WithTags("Learning")
            .RequireAuthorization();

        learnerGroup.MapGet("/by-episode/{episodeId:guid}", GetByEpisodeForLearnerAsync)
            .WithName("LearningGetQuizByEpisode")
            .WithSummary("ดึงแบบทดสอบสำหรับบทเรียน (สำหรับผู้เรียน คำตอบถูกจะถูกซ่อนไว้)")
            .Produces<LearnerQuizResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> GetByEpisodeForLearnerAsync(
        Guid episodeId,
        QuizService service,
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
        CreateQuizRequest request,
        QuizService service,
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
            ? Results.Created($"/api/learning/instructor/quizzes/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        QuizService service,
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

    private static async Task<IResult> AddQuestionAsync(
        Guid id,
        AddQuizQuestionRequest request,
        QuizService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.AddQuestionAsync(userId, id, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Created($"/api/learning/instructor/quizzes/{id}", result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> AddOptionAsync(
        Guid quizId,
        Guid questionId,
        AddQuizOptionRequest request,
        QuizService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.AddOptionAsync(userId, quizId, questionId, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Created($"/api/learning/instructor/quizzes/{quizId}", result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> ActivateAsync(
        Guid id,
        QuizService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.ActivateAsync(userId, id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> DeactivateAsync(
        Guid id,
        QuizService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.DeactivateAsync(userId, id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }
}
