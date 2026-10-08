using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.Routing;
using Siri.Api.Configuration;
using Siri.Modules.Catalog.Features.AttachSessionRecording;
using Siri.Modules.Catalog.Features.CancelLiveSession;
using Siri.Modules.Catalog.Features.CreateLiveSession;
using Siri.Modules.Catalog.Features.UpdateLiveSession;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Catalog;

[ApiController]
[Route("api/catalog/instructor/courses/{courseId:guid}/live-sessions")]
[Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
[EnableRateLimiting("default")]
[Tags("Catalog")]
public class LiveSessionsController : ControllerBase
{
    [HttpPost("")]
    [EndpointName("CatalogCreateLiveSession")]
    [EndpointSummary("สร้างคาบสอนสดใหม่")]
    [ProducesResponseType(typeof(LiveSessionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> CreateLiveSession(
        [FromRoute] Guid courseId,
        [FromBody] CreateLiveSessionCommand command,
        [FromServices] CreateLiveSessionHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, courseId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/catalog/instructor/courses/{courseId}/live-sessions/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPut("{sessionId:guid}")]
    [EndpointName("CatalogUpdateLiveSession")]
    [EndpointSummary("แก้ไขเวลาและข้อมูลคาบสอนสด")]
    [ProducesResponseType(typeof(LiveSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> UpdateLiveSession(
        [FromRoute] Guid courseId,
        [FromRoute] Guid sessionId,
        [FromBody] UpdateLiveSessionCommand command,
        [FromServices] UpdateLiveSessionHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, courseId, sessionId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpDelete("{sessionId:guid}")]
    [EndpointName("CatalogDeleteLiveSession")]
    [EndpointSummary("ยกเลิกคาบสอนสด (ไม่มีเหตุผล)")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> DeleteLiveSession(
        [FromRoute] Guid courseId,
        [FromRoute] Guid sessionId,
        [FromServices] CancelLiveSessionHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.CancelAsync(userId, courseId, sessionId, reason: null, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.NoContent()
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("{sessionId:guid}/cancel")]
    [EndpointName("CatalogCancelLiveSessionWithReason")]
    [EndpointSummary("ยกเลิกคาบสอนสดพร้อมระบุเหตุผล")]
    [ProducesResponseType(typeof(LiveSessionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> CancelLiveSessionWithReason(
        [FromRoute] Guid courseId,
        [FromRoute] Guid sessionId,
        [FromBody] CancelLiveSessionCommand command,
        [FromServices] CancelLiveSessionHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.CancelAsync(userId, courseId, sessionId, command.Reason, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    /// <summary>
    /// P11-06: turns an uploaded recording into an ordinary lesson of the course, which is what makes catch-up work (no separate entitlement rule).
    /// Uses the per-user <c>live-user</c> limiter at action level, overriding the class-level fixed-window "default" (docs/contracts/P11-06 §0 F7).
    /// </summary>
    [HttpPost("{sessionId:guid}/recording")]
    [EnableRateLimiting(RateLimiterConfiguration.LiveUserPolicyName)]
    [EndpointName("CatalogAttachLiveSessionRecording")]
    [EndpointSummary("แนบบันทึกการสอนเข้าคาบสอนสดที่เริ่มแล้ว (กลายเป็นบทเรียนปกติของคอร์ส = ดูย้อนหลัง)")]
    [ProducesResponseType(typeof(LiveSessionRecordingResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status429TooManyRequests)]
    public async Task<IResult> AttachSessionRecording(
        [FromRoute] Guid courseId,
        [FromRoute] Guid sessionId,
        [FromBody] AttachSessionRecordingCommand command,
        [FromServices] AttachSessionRecordingHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, courseId, sessionId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }
}
