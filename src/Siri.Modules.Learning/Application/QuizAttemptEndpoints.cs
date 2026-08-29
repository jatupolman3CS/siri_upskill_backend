using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// Maps the learner-facing endpoints for <see cref="Domain.QUIZ_ATTEMPT"/>. Self-contained group — see
/// <see cref="QuizEndpoints"/>'s own doc comment for why. Default-authenticated only (no extra role) —
/// any learner may attempt a quiz; per-attempt ownership is a <see cref="QuizAttemptService"/> concern
/// (see that class's own doc comment for the real check a later task must add), same "role gate is not
/// the same as a per-resource ownership check" split Catalog's course endpoints already draw.
/// </summary>
public static class QuizAttemptEndpoints
{
    public static IEndpointRouteBuilder MapQuizAttemptEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/learning/quiz-attempts")
            .WithTags("Learning")
            .RequireAuthorization();

        group.MapPost("/", StartAsync)
            .AddEndpointFilter<ValidationEndpointFilter<StartQuizAttemptRequest>>()
            .WithName("LearningStartQuizAttempt")
            .WithSummary("เริ่มทำแบบทดสอบ")
            .Produces<QuizAttemptResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        group.MapGet("/{id:guid}", GetByIdAsync)
            .WithName("LearningGetQuizAttempt")
            .WithSummary("ดูผลการทำแบบทดสอบครั้งนี้")
            .Produces<QuizAttemptResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapPost("/{id:guid}/answers", AnswerAsync)
            .AddEndpointFilter<ValidationEndpointFilter<SubmitQuizAnswerRequest>>()
            .WithName("LearningAnswerQuizQuestion")
            .WithSummary("บันทึกคำตอบของคำถามหนึ่งข้อ")
            .Produces<QuizAttemptAnswerResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        group.MapPost("/{id:guid}/submit", SubmitAsync)
            .WithName("LearningSubmitQuizAttempt")
            .WithSummary("ส่งคำตอบทั้งหมดเพื่อตรวจให้คะแนน")
            .Produces<QuizAttemptResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        group.MapGet("/by-quiz/{quizId:guid}", ListByQuizAsync)
            .WithName("LearningListQuizAttemptsByQuiz")
            .WithSummary("ดูประวัติการทำแบบทดสอบทั้งหมดของตัวเองในแบบทดสอบนี้")
            .Produces<IReadOnlyList<QuizAttemptResponse>>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> ListByQuizAsync(
        Guid quizId,
        QuizAttemptService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.ListAttemptsByQuizAsync(userId, quizId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> StartAsync(
        StartQuizAttemptRequest request,
        QuizAttemptService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.StartAsync(userId, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Created($"/api/learning/quiz-attempts/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> GetByIdAsync(
        Guid id,
        QuizAttemptService service,
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

    private static async Task<IResult> AnswerAsync(
        Guid id,
        SubmitQuizAnswerRequest request,
        QuizAttemptService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.AnswerAsync(userId, id, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> SubmitAsync(
        Guid id,
        QuizAttemptService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.SubmitAsync(userId, id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }
}
