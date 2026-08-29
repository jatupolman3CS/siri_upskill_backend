using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Learning.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Learning;

[ApiController]
[Route("api/learning/quiz-attempts")]
[Authorize]
[Tags("Learning")]
public class QuizAttemptsController : ControllerBase
{
    [HttpPost("")]
    [EndpointName("LearningStartQuizAttempt")]
    [EndpointSummary("เริ่มทำแบบทดสอบ")]
    [ProducesResponseType(typeof(QuizAttemptResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> Start(
        [FromBody] StartQuizAttemptRequest request,
        [FromServices] QuizAttemptService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.StartAsync(userId, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Created($"/api/learning/quiz-attempts/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("{id:guid}")]
    [EndpointName("LearningGetQuizAttempt")]
    [EndpointSummary("ดูผลการทำแบบทดสอบครั้งนี้")]
    [ProducesResponseType(typeof(QuizAttemptResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetById(
        [FromRoute] Guid id,
        [FromServices] QuizAttemptService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetByIdAsync(userId, id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("{id:guid}/answers")]
    [EndpointName("LearningAnswerQuizQuestion")]
    [EndpointSummary("บันทึกคำตอบของคำถามหนึ่งข้อ")]
    [ProducesResponseType(typeof(QuizAttemptAnswerResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> Answer(
        [FromRoute] Guid id,
        [FromBody] SubmitQuizAnswerRequest request,
        [FromServices] QuizAttemptService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.AnswerAsync(userId, id, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("{id:guid}/submit")]
    [EndpointName("LearningSubmitQuizAttempt")]
    [EndpointSummary("ส่งคำตอบทั้งหมดเพื่อตรวจให้คะแนน")]
    [ProducesResponseType(typeof(QuizAttemptResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> Submit(
        [FromRoute] Guid id,
        [FromServices] QuizAttemptService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.SubmitAsync(userId, id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("by-quiz/{quizId:guid}")]
    [EndpointName("LearningListQuizAttemptsByQuiz")]
    [EndpointSummary("ดูประวัติการทำแบบทดสอบทั้งหมดของตัวเองในแบบทดสอบนี้")]
    [ProducesResponseType(typeof(IReadOnlyList<QuizAttemptResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> ListByQuiz(
        [FromRoute] Guid quizId,
        [FromServices] QuizAttemptService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.ListAttemptsByQuizAsync(userId, quizId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }
}
