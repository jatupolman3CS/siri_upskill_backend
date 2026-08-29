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
public class CertificatesController : ControllerBase
{
    [HttpGet("certificates/verify/{verifyCode}")]
    [AllowAnonymous]
    [EndpointName("LearningVerifyCertificate")]
    [EndpointSummary("ตรวจสอบความถูกต้องของใบประกาศนียบัตรด้วยรหัสยืนยัน (ไม่ต้องล็อกอิน)")]
    [ProducesResponseType(typeof(CertificateVerificationResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> VerifyByCode(
        [FromRoute] string verifyCode,
        [FromServices] CertificateService service,
        CancellationToken cancellationToken)
    {
        var result = await service.VerifyByCodeAsync(verifyCode, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("certificates/verify/{verifyCode}/download")]
    [AllowAnonymous]
    [EndpointName("LearningDownloadCertificatePdfByVerifyCode")]
    [EndpointSummary("ดาวน์โหลดไฟล์ PDF ใบประกาศนียบัตรด้วยรหัสยืนยัน (ไม่ต้องล็อกอิน)")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileContentResult))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> DownloadPdfByVerifyCode(
        [FromRoute] string verifyCode,
        [FromServices] CertificateService service,
        CancellationToken cancellationToken)
    {
        var result = await service.GeneratePdfByVerifyCodeAsync(verifyCode, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return result.Error.ToProblemHttpResult(HttpContext);
        }

        return Results.File(result.Value.Bytes, "application/pdf", result.Value.FileName);
    }

    [HttpGet("certificates/me")]
    [Authorize]
    [EndpointName("LearningListMyCertificates")]
    [EndpointSummary("รายการใบประกาศนียบัตรของตัวเอง")]
    [ProducesResponseType(typeof(PagedResult<CertificateResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IResult> ListMine(
        [FromServices] CertificateService service,
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

    [HttpGet("certificates/{id:guid}")]
    [Authorize]
    [EndpointName("LearningGetMyCertificate")]
    [EndpointSummary("ดูรายละเอียดใบประกาศนียบัตรของตัวเอง")]
    [ProducesResponseType(typeof(CertificateDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetOwn(
        [FromRoute] Guid id,
        [FromServices] CertificateService service,
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

    [HttpGet("certificates/{id:guid}/download")]
    [Authorize]
    [EndpointName("LearningDownloadCertificatePdf")]
    [EndpointSummary("ดาวน์โหลดไฟล์ PDF ใบประกาศนียบัตร")]
    [ProducesResponseType(StatusCodes.Status200OK, Type = typeof(FileContentResult))]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> DownloadPdf(
        [FromRoute] Guid id,
        [FromServices] CertificateService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        var userId = userContext.UserId;
        var isAdmin = userContext.Roles.Contains(RoleNames.Admin) || userContext.Roles.Contains(RoleNames.SuperAdmin);

        var result = await service.GeneratePdfAsync(id, userId, isAdmin, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return result.Error.ToProblemHttpResult(HttpContext);
        }

        return Results.File(result.Value.Bytes, "application/pdf", result.Value.FileName);
    }

    [HttpGet("certificates/by-enrollment/{enrollmentId:guid}")]
    [Authorize]
    [EndpointName("LearningGetCertificateByEnrollment")]
    [EndpointSummary("ดูใบประกาศนียบัตรตามการลงทะเบียนเรียน")]
    [ProducesResponseType(typeof(CertificateDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetByEnrollment(
        [FromRoute] Guid enrollmentId,
        [FromServices] CertificateService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetByEnrollmentIdAsync(userId, enrollmentId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("certificates/by-course/{courseId:guid}")]
    [Authorize]
    [EndpointName("LearningGetCertificateByCourse")]
    [EndpointSummary("ดูใบประกาศนียบัตรตามคอร์สเรียน")]
    [ProducesResponseType(typeof(CertificateDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetByCourse(
        [FromRoute] Guid courseId,
        [FromServices] CertificateService service,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetByCourseIdAsync(userId, courseId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("admin/certificates")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("LearningIssueCertificate")]
    [EndpointSummary("ออกใบประกาศนียบัตร (ops/backfill/ออกใหม่)")]
    [ProducesResponseType(typeof(CertificateResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> Create(
        [FromBody] IssueCertificateCommand command,
        [FromServices] CertificateService service,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/learning/admin/certificates/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("admin/certificates")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("LearningListCertificates")]
    [EndpointSummary("รายการใบประกาศนียบัตรทั้งหมด (กรองตามการลงทะเบียนได้)")]
    [ProducesResponseType(typeof(PagedResult<CertificateResponse>), StatusCodes.Status200OK)]
    public async Task<IResult> List(
        [FromServices] CertificateService service,
        [FromQuery] Guid? enrollmentId = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await service.ListAsync(enrollmentId, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    [HttpPost("admin/certificates/{id:guid}/revoke")]
    [Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
    [EndpointName("LearningRevokeCertificate")]
    [EndpointSummary("เพิกถอนใบประกาศนียบัตร")]
    [ProducesResponseType(typeof(CertificateResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> Revoke(
        [FromRoute] Guid id,
        [FromServices] CertificateService service,
        CancellationToken cancellationToken)
    {
        var result = await service.RevokeAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }
}
