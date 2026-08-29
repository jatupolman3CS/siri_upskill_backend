using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// Maps every <see cref="Domain.CERTIFICATE"/> HTTP endpoint. Self-contained top-level
/// <c>/api/learning</c> <c>MapGroup</c>.
/// </summary>
public static class CertificateEndpoints
{
    public static IEndpointRouteBuilder MapCertificateEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/learning").WithTags("Learning").RequireAuthorization();

        var mine = group.MapGroup("/certificates");

        mine.MapGet("/verify/{verifyCode}", VerifyByCodeAsync)
            .AllowAnonymous()
            .WithName("LearningVerifyCertificate")
            .WithSummary("ตรวจสอบความถูกต้องของใบประกาศนียบัตรด้วยรหัสยืนยัน (ไม่ต้องล็อกอิน)")
            .Produces<CertificateVerificationResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        mine.MapGet("/verify/{verifyCode}/download", DownloadPdfByVerifyCodeAsync)
            .AllowAnonymous()
            .WithName("LearningDownloadCertificatePdfByVerifyCode")
            .WithSummary("ดาวน์โหลดไฟล์ PDF ใบประกาศนียบัตรด้วยรหัสยืนยัน (ไม่ต้องล็อกอิน)")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        mine.MapGet("/me", ListMineAsync)
            .WithName("LearningListMyCertificates")
            .WithSummary("รายการใบประกาศนียบัตรของตัวเอง")
            .Produces<PagedResult<CertificateResponse>>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized);

        mine.MapGet("/{id:guid}", GetOwnAsync)
            .WithName("LearningGetMyCertificate")
            .WithSummary("ดูรายละเอียดใบประกาศนียบัตรของตัวเอง")
            .Produces<CertificateDetailResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        mine.MapGet("/{id:guid}/download", DownloadPdfAsync)
            .WithName("LearningDownloadCertificatePdf")
            .WithSummary("ดาวน์โหลดไฟล์ PDF ใบประกาศนียบัตร")
            .Produces(StatusCodes.Status200OK, contentType: "application/pdf")
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        mine.MapGet("/by-enrollment/{enrollmentId:guid}", GetByEnrollmentAsync)
            .WithName("LearningGetCertificateByEnrollment")
            .WithSummary("ดูใบประกาศนียบัตรตามการลงทะเบียนเรียน")
            .Produces<CertificateDetailResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        mine.MapGet("/by-course/{courseId:guid}", GetByCourseAsync)
            .WithName("LearningGetCertificateByCourse")
            .WithSummary("ดูใบประกาศนียบัตรตามคอร์สเรียน")
            .Produces<CertificateDetailResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        var admin = group.MapGroup("/admin/certificates").RequireAuthorization(AuthorizationPolicyNames.AdminOnly);

        admin.MapPost("/", CreateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<IssueCertificateCommand>>()
            .WithName("LearningIssueCertificate")
            .WithSummary("ออกใบประกาศนียบัตร (ops/backfill/ออกใหม่)")
            .Produces<CertificateResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        admin.MapGet("/", ListAsync)
            .WithName("LearningListCertificates")
            .WithSummary("รายการใบประกาศนียบัตรทั้งหมด (กรองตามการลงทะเบียนได้)")
            .Produces<PagedResult<CertificateResponse>>(StatusCodes.Status200OK);

        admin.MapPost("/{id:guid}/revoke", RevokeAsync)
            .WithName("LearningRevokeCertificate")
            .WithSummary("เพิกถอนใบประกาศนียบัตร")
            .Produces<CertificateResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> VerifyByCodeAsync(
        string verifyCode,
        CertificateService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.VerifyByCodeAsync(verifyCode, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> CreateAsync(
        IssueCertificateCommand command,
        CertificateService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.CreateAsync(command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/learning/admin/certificates/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> ListAsync(
        CertificateService service,
        CancellationToken cancellationToken,
        Guid? enrollmentId = null,
        int page = 1,
        int pageSize = 20)
    {
        var result = await service.ListAsync(enrollmentId, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> ListMineAsync(
        CertificateService service,
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
        CertificateService service,
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

    private static async Task<IResult> GetByEnrollmentAsync(
        Guid enrollmentId,
        CertificateService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetByEnrollmentIdAsync(userId, enrollmentId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> GetByCourseAsync(
        Guid courseId,
        CertificateService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetByCourseIdAsync(userId, courseId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> DownloadPdfAsync(
        Guid id,
        CertificateService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var userId = userContext.UserId;
        var isAdmin = userContext.Roles.Contains(RoleNames.Admin) || userContext.Roles.Contains(RoleNames.SuperAdmin);

        var result = await service.GeneratePdfAsync(id, userId, isAdmin, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return result.Error.ToProblemHttpResult(httpContext);
        }

        return Results.File(result.Value.Bytes, "application/pdf", result.Value.FileName);
    }

    private static async Task<IResult> DownloadPdfByVerifyCodeAsync(
        string verifyCode,
        CertificateService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.GeneratePdfByVerifyCodeAsync(verifyCode, cancellationToken).ConfigureAwait(false);
        if (!result.IsSuccess)
        {
            return result.Error.ToProblemHttpResult(httpContext);
        }

        return Results.File(result.Value.Bytes, "application/pdf", result.Value.FileName);
    }

    private static async Task<IResult> RevokeAsync(
        Guid id,
        CertificateService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.RevokeAsync(id, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }
}
