using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Media.Application;

/// <summary>
/// Maps the upload-session endpoints — creation nests under its parent asset
/// (<c>POST /api/media/assets/{mediaAssetId}/upload-sessions</c>, mapped via
/// <see cref="MapCreateMediaUploadSessionEndpoint"/> onto the same <c>assets</c> group as
/// <see cref="MediaAssetEndpoints"/>), while Get/Complete are top-level under their own id
/// (<c>/api/media/upload-sessions/{id}</c>, via <see cref="MapMediaUploadSessionEndpoints"/>) — see
/// <c>MediaModule.MapMediaEndpoints</c> for exactly how the groups nest and which policy applies. No
/// request body on create — see <see cref="MediaUploadSessionService.CreateAsync"/>'s own doc comment for
/// why (same "no bindable command" shape Catalog already uses for actions with no meaningful client input,
/// e.g. <c>SubmitCourseForReviewEndpoint</c>).
/// </summary>
public static class MediaUploadSessionEndpoints
{
    /// <summary>Mapped onto the <c>/api/media/assets</c> group — see class doc comment.</summary>
    public static IEndpointRouteBuilder MapCreateMediaUploadSessionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/{mediaAssetId:guid}/upload-sessions", HandleCreateAsync)
            .WithName("MediaCreateUploadSession")
            .WithSummary("เริ่มเซสชันอัปโหลดใหม่สำหรับวิดีโอนี้")
            .Produces<MediaUploadSessionResponse>(StatusCodes.Status201Created)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>Mapped onto the <c>/api/media/upload-sessions</c> group — see class doc comment.</summary>
    public static IEndpointRouteBuilder MapMediaUploadSessionEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/{id:guid}", HandleGetByIdAsync)
            .WithName("MediaGetUploadSession")
            .WithSummary("รายละเอียดเซสชันอัปโหลด")
            .Produces<MediaUploadSessionResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        endpoints.MapPost("/{id:guid}/complete", HandleCompleteAsync)
            .WithName("MediaCompleteUploadSession")
            .WithSummary("แจ้งว่าอัปโหลดเสร็จแล้ว")
            .Produces<MediaUploadSessionResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(
        Guid mediaAssetId,
        MediaUploadSessionService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.CreateAsync(userId, mediaAssetId, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/media/upload-sessions/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleGetByIdAsync(
        Guid id,
        MediaUploadSessionService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetByIdAsync(userId, id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleCompleteAsync(
        Guid id,
        MediaUploadSessionService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.CompleteAsync(userId, id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
