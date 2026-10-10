using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Catalog.Features.AddEpisodeAttachment;
using Siri.Modules.Catalog.Features.DeleteEpisodeAttachment;
using Siri.Modules.Catalog.Features.DownloadEpisodeAttachment;
using Siri.Modules.Catalog.Features.GetEpisodeAttachments;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features;

public static class EpisodeAttachmentEndpoints
{
    public static IEndpointRouteBuilder MapEpisodeAttachmentEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/episodes/{episodeId:guid}/attachments");

        group.MapGet("/", async (
            Guid episodeId,
            GetEpisodeAttachmentsHandler handler,
            IUserContext userContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var isAdmin = httpContext.User.IsInRole(RoleNames.Admin) || httpContext.User.IsInRole(RoleNames.SuperAdmin);
            var result = await handler.HandleAsync(episodeId, userContext.UserId, isAdmin, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
        })
        .AllowAnonymous()
        .WithName("GetEpisodeAttachments")
        .WithSummary("ดึงรายการไฟล์แนบของบทเรียน (ตรวจสิทธิ์การลงทะเบียน/ตัวอย่างฟรี)")
        .Produces<IReadOnlyList<EpisodeAttachmentResponse>>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapGet("/{attachmentId:guid}/download", async (
            Guid episodeId,
            Guid attachmentId,
            DownloadEpisodeAttachmentHandler handler,
            IUserContext userContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var isAdmin = httpContext.User.IsInRole(RoleNames.Admin) || httpContext.User.IsInRole(RoleNames.SuperAdmin);
            var result = await handler.HandleAsync(episodeId, attachmentId, userContext.UserId, isAdmin, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
        })
        .AllowAnonymous()
        .WithName("DownloadEpisodeAttachment")
        .WithSummary("ขอรับลิงก์ดาวน์โหลดไฟล์แนบของบทเรียน (ตรวจสิทธิ์การลงทะเบียน/ตัวอย่างฟรี)")
        .Produces<EpisodeAttachmentDownloadResponse>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapPost("/", async (
            Guid episodeId,
            IFormFile file,
            AddEpisodeAttachmentHandler handler,
            IUserContext userContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var isAdmin = httpContext.User.IsInRole(RoleNames.Admin) || httpContext.User.IsInRole(RoleNames.SuperAdmin);

            await using var content = file.OpenReadStream();
            var command = new AddEpisodeAttachmentCommand(file.FileName, file.ContentType, content);
            var result = await handler.HandleAsync(episodeId, command, userContext.UserId, isAdmin, cancellationToken).ConfigureAwait(false);

            return result.IsSuccess
                ? Results.Created($"/api/catalog/episodes/{episodeId}/attachments/{result.Value.Id}", result.Value)
                : result.Error.ToProblemHttpResult(httpContext);
        })
        // Bearer-token API, not a cookie-authenticated form: antiforgery protects nothing here.
        .DisableAntiforgery()
        .WithName("AddEpisodeAttachment")
        .WithSummary("อัปโหลดไฟล์แนบให้บทเรียนไปที่ R2 (ผู้สอนเจ้าของบทเรียน หรือแอดมิน)")
        .Accepts<IFormFile>("multipart/form-data")
        .Produces<EpisodeAttachmentResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
        .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
        .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapDelete("/{attachmentId:guid}", async (
            Guid episodeId,
            Guid attachmentId,
            DeleteEpisodeAttachmentHandler handler,
            IUserContext userContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var isAdmin = httpContext.User.IsInRole(RoleNames.Admin) || httpContext.User.IsInRole(RoleNames.SuperAdmin);
            var result = await handler.HandleAsync(episodeId, attachmentId, userContext.UserId, isAdmin, cancellationToken).ConfigureAwait(false);

            return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemHttpResult(httpContext);
        })
        .WithName("DeleteEpisodeAttachment")
        .WithSummary("ลบไฟล์แนบของบทเรียน")
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
        .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
        .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
