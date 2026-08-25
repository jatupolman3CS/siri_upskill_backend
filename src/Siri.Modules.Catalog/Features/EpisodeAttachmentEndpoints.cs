using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Catalog.Features.AddEpisodeAttachment;
using Siri.Modules.Catalog.Features.DeleteEpisodeAttachment;
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
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(episodeId, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
        })
        .WithName("GetEpisodeAttachments")
        .WithSummary("ดึงรายการไฟล์แนบของบทเรียน")
        .Produces<IReadOnlyList<EpisodeAttachmentResponse>>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapPost("/", async (
            Guid episodeId,
            AddEpisodeAttachmentCommand command,
            AddEpisodeAttachmentHandler handler,
            IUserContext userContext,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var isAdmin = httpContext.User.IsInRole("Admin");
            var result = await handler.HandleAsync(episodeId, command, userContext.UserId, isAdmin, cancellationToken).ConfigureAwait(false);

            return result.IsSuccess
                ? Results.Created($"/api/catalog/episodes/{episodeId}/attachments/{result.Value.Id}", result.Value)
                : result.Error.ToProblemHttpResult(httpContext);
        })
        .AddEndpointFilter<ValidationEndpointFilter<AddEpisodeAttachmentCommand>>()
        .WithName("AddEpisodeAttachment")
        .WithSummary("เพิ่มไฟล์แนบให้บทเรียน (ผู้สอนเจ้าของบทเรียน หรือแอดมิน)")
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
            var isAdmin = httpContext.User.IsInRole("Admin");
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
