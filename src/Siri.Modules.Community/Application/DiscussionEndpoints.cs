using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Community.Application.Response;
using Siri.SharedKernel;

namespace Siri.Modules.Community.Application;

/// <summary>
/// Maps every <c>DISCUSSION</c> HTTP endpoint — one file per entity for this module (unlike Catalog's
/// one-file-per-feature vertical slices), matching this task's Repository+Service folder layout. Route
/// groups/policies are applied by the caller (<c>CommunityModule.MapCommunityEndpoints</c>), same
/// "endpoint just maps its own relative route, the module composes the group prefix + policy" split
/// Catalog's <c>CatalogModule.MapCatalogEndpoints</c> and Payout's <c>RevenueSplitEndpoints</c> already use.
/// <para>
/// Every handler delegate below calls straight into <see cref="DiscussionService"/>, which is implemented
/// for real and wired into <c>Siri.Api/Program.cs</c> as of 2026-08-24 — the old "scaffold, throws
/// NotImplementedException" note here was stale and has been removed.
/// </para>
/// </summary>
public static class DiscussionEndpoints
{
    /// <summary>Maps POST /api/community/discussions.</summary>
    public static IEndpointRouteBuilder MapCreateDiscussionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/", HandleCreateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreateDiscussionCommand>>()
            .WithName("CommunityCreateDiscussion")
            .WithSummary("ตั้งกระทู้ใหม่หรือตอบกลับกระทู้เดิมในบทเรียน")
            .Produces<DiscussionResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    /// <summary>Maps GET /api/community/discussions?episodeId=&amp;page=&amp;pageSize=.</summary>
    public static IEndpointRouteBuilder MapListDiscussionsByEpisodeEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", HandleListByEpisodeAsync)
            .WithName("CommunityListDiscussionsByEpisode")
            .WithSummary("รายการถาม-ตอบของบทเรียนหนึ่งบท")
            .Produces<PagedResult<DiscussionResponse>>(StatusCodes.Status200OK);

        return endpoints;
    }

    /// <summary>Maps POST /api/community/discussions/{id}/upvote.</summary>
    public static IEndpointRouteBuilder MapUpvoteDiscussionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/{id:guid}/upvote", HandleUpvoteAsync)
            .WithName("CommunityUpvoteDiscussion")
            .WithSummary("โหวตว่ากระทู้นี้มีประโยชน์")
            .Produces<DiscussionResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>Maps DELETE /api/community/discussions/{id} — caller's own post only, ownership check is
    /// <see cref="DiscussionService.DeleteAsync"/>'s job (see that method's own doc comment).</summary>
    public static IEndpointRouteBuilder MapDeleteDiscussionEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDelete("/{id:guid}", HandleDeleteAsync)
            .WithName("CommunityDeleteDiscussion")
            .WithSummary("ลบกระทู้ของตัวเอง")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(
        CreateDiscussionCommand command,
        DiscussionService service,
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
            ? Results.Created($"/api/community/discussions/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleListByEpisodeAsync(
        Guid episodeId,
        DiscussionService service,
        HttpContext httpContext,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = DiscussionService.DefaultPageSize)
    {
        var result = await service.ListByEpisodeAsync(episodeId, page, pageSize, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleUpvoteAsync(
        Guid id,
        DiscussionService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.UpvoteAsync(userId, id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleDeleteAsync(
        Guid id,
        DiscussionService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.DeleteAsync(userId, id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemHttpResult(httpContext);
    }
}
