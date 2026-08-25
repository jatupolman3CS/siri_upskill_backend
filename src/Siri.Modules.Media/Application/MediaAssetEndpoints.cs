using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Media.Application;

/// <summary>
/// Maps the <c>/api/media/assets</c> endpoints (see <c>MediaModule.MapMediaEndpoints</c> for the group
/// prefix + <see cref="AuthorizationPolicyNames.InstructorOnly"/> policy). No endpoint for
/// <see cref="MediaAssetService.UpdateStatusAsync"/> — that transition is provider/pipeline-driven (a
/// future Bunny Stream webhook or polling job calls the service method directly), not a client action, same
/// reasoning <c>UpdateMediaAssetStatusCommand</c>'s own doc comment gives.
/// <para><see cref="MediaAssetService"/> is implemented for real as of 2026-08-24 — the old D-17 scaffold
/// note here was stale and has been removed.</para>
/// </summary>
public static class MediaAssetEndpoints
{
    public static IEndpointRouteBuilder MapMediaAssetEndpoints(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/", HandleCreateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreateMediaAssetCommand>>()
            .WithName("MediaCreateAsset")
            .WithSummary("ลงทะเบียนวิดีโอใหม่เพื่อเริ่มอัปโหลด")
            .Produces<MediaAssetResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem();

        endpoints.MapGet("/", HandleGetMyAssetsAsync)
            .WithName("MediaGetMyAssets")
            .WithSummary("รายการวิดีโอของตัวเอง")
            .Produces<PagedResult<MediaAssetResponse>>(StatusCodes.Status200OK);

        endpoints.MapGet("/{id:guid}", HandleGetByIdAsync)
            .WithName("MediaGetAsset")
            .WithSummary("รายละเอียดวิดีโอ")
            .Produces<MediaAssetResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        endpoints.MapDelete("/{id:guid}", HandleDeleteAsync)
            .WithName("MediaDeleteAsset")
            .WithSummary("ลบวิดีโอ")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status403Forbidden)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(
        CreateMediaAssetCommand command,
        MediaAssetService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.CreateAsync(userId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/media/assets/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleGetMyAssetsAsync(
        MediaAssetService service,
        IUserContext userContext,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = MediaAssetService.DefaultPageSize)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetMyAssetsAsync(userId, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> HandleGetByIdAsync(
        Guid id,
        MediaAssetService service,
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

    private static async Task<IResult> HandleDeleteAsync(
        Guid id,
        MediaAssetService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.DeleteAsync(userId, id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.NoContent()
            : result.Error.ToProblemHttpResult(httpContext);
    }
}
