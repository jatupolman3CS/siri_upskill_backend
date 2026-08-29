using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Learning.Application;
using Siri.Modules.Learning.Domain;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Learning;

[ApiController]
[Route("api/learning")]
[Authorize]
[Tags("Learning")]
public class EnrollmentsController : ControllerBase
{
    [HttpGet("enrollments/me")]
    [EndpointName("LearningListMyEnrollments")]
    [EndpointSummary("รายการคอร์สที่ตัวเองลงทะเบียนไว้")]
    [ProducesResponseType(typeof(PagedResult<EnrollmentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> ListMine(
        [FromServices] EnrollmentService service,
        [FromServices] IUserContext userContext,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.ListForUserAsync(userId, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    [HttpGet("enrollments/{id:guid}")]
    [EndpointName("LearningGetMyEnrollment")]
    [EndpointSummary("ดูรายละเอียดการลงทะเบียนของตัวเอง")]
    [ProducesResponseType(typeof(EnrollmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetOwn(
        [FromRoute] Guid id,
        [FromServices] EnrollmentService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetOwnAsync(userId, id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPut("enrollments/{id:guid}/progress")]
    [EndpointName("LearningUpdateMyEnrollmentProgress")]
    [EndpointSummary("อัปเดตความคืบหน้าการเรียนของตัวเอง")]
    [ProducesResponseType(typeof(EnrollmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> UpdateOwnProgress(
        [FromRoute] Guid id,
        [FromBody] UpdateEnrollmentProgressCommand command,
        [FromServices] EnrollmentService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.UpdateOwnProgressAsync(userId, id, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("admin/enrollments")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("LearningCreateEnrollment")]
    [EndpointSummary("สร้างการลงทะเบียนเรียน (ops/backfill/ของขวัญ)")]
    [ProducesResponseType(typeof(EnrollmentResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> Create(
        [FromBody] CreateEnrollmentCommand command,
        [FromServices] EnrollmentService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/learning/admin/enrollments/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("admin/enrollments")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("LearningListEnrollments")]
    [EndpointSummary("รายการการลงทะเบียนทั้งหมด (กรองตามคอร์ส/สถานะได้)")]
    [ProducesResponseType(typeof(PagedResult<EnrollmentResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> List(
        [FromServices] EnrollmentService service,
        [FromQuery] Guid? courseId = null,
        [FromQuery] EnrollmentStatus? status = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await service.ListAsync(courseId, status, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    [HttpPost("admin/enrollments/{id:guid}/revoke")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("LearningRevokeEnrollment")]
    [EndpointSummary("เพิกถอนสิทธิ์การเรียน")]
    [ProducesResponseType(typeof(EnrollmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> Revoke(
        [FromRoute] Guid id,
        [FromServices] EnrollmentService service,
        CancellationToken cancellationToken)
    {
        var result = await service.RevokeAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }
}
