using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Learning.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Learning;

[ApiController]
[Route("api/learning")]
[Tags("Learning")]
public class QuizzesController : ControllerBase
{
    [HttpPost("instructor/quizzes")]
    [Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
    [EndpointName("LearningCreateQuiz")]
    [EndpointSummary("สร้างแบบทดสอบใหม่สำหรับบทเรียน")]
    [ProducesResponseType(typeof(QuizResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    public async Task<IResult> Create(
        [FromBody] CreateQuizRequest request,
        [FromServices] QuizService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.CreateAsync(userId, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess
            ? Results.Created($"/api/learning/instructor/quizzes/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("instructor/quizzes/{id:guid}")]
    [Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
    [EndpointName("LearningGetQuiz")]
    [EndpointSummary("ดูแบบทดสอบพร้อมคำถาม/ตัวเลือกทั้งหมด")]
    [ProducesResponseType(typeof(QuizResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetById(
        [FromRoute] Guid id,
        [FromServices] QuizService service,
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

    [HttpPost("instructor/quizzes/{id:guid}/questions")]
    [Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
    [EndpointName("LearningAddQuizQuestion")]
    [EndpointSummary("เพิ่มคำถามในแบบทดสอบ")]
    [ProducesResponseType(typeof(QuizQuestionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> AddQuestion(
        [FromRoute] Guid id,
        [FromBody] AddQuizQuestionRequest request,
        [FromServices] QuizService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.AddQuestionAsync(userId, id, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Created($"/api/learning/instructor/quizzes/{id}", result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("instructor/quizzes/{quizId:guid}/questions/{questionId:guid}/options")]
    [Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
    [EndpointName("LearningAddQuizOption")]
    [EndpointSummary("เพิ่มตัวเลือกคำตอบในคำถาม")]
    [ProducesResponseType(typeof(QuizOptionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> AddOption(
        [FromRoute] Guid quizId,
        [FromRoute] Guid questionId,
        [FromBody] AddQuizOptionRequest request,
        [FromServices] QuizService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.AddOptionAsync(userId, quizId, questionId, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Created($"/api/learning/instructor/quizzes/{quizId}", result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("instructor/quizzes/{id:guid}/activate")]
    [Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
    [EndpointName("LearningActivateQuiz")]
    [EndpointSummary("เปิดใช้งานแบบทดสอบ")]
    [ProducesResponseType(typeof(QuizResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> Activate(
        [FromRoute] Guid id,
        [FromServices] QuizService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.ActivateAsync(userId, id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("instructor/quizzes/{id:guid}/deactivate")]
    [Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
    [EndpointName("LearningDeactivateQuiz")]
    [EndpointSummary("ปิดใช้งานแบบทดสอบ")]
    [ProducesResponseType(typeof(QuizResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> Deactivate(
        [FromRoute] Guid id,
        [FromServices] QuizService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.DeactivateAsync(userId, id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("quizzes/by-episode/{episodeId:guid}")]
    [Authorize]
    [EndpointName("LearningGetQuizByEpisode")]
    [EndpointSummary("ดึงแบบทดสอบสำหรับบทเรียน (สำหรับผู้เรียน คำตอบถูกจะถูกซ่อนไว้)")]
    [ProducesResponseType(typeof(LearnerQuizResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetByEpisodeForLearner(
        [FromRoute] Guid episodeId,
        [FromServices] QuizService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetByEpisodeForLearnerAsync(userId, episodeId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }
}
