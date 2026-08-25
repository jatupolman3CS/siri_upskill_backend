using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Cms.Application;

/// <summary>
/// Maps every <see cref="Domain.POST"/> HTTP endpoint — see <see cref="BannerEndpoints"/>'s own doc comment
/// for the shared file-per-entity/group-composed-by-caller shape and the "every handler is a stub today"
/// note.
/// <para>
/// Unlike Banner/MenuItem/Redirect, this entity has a public read side (see <c>CmsModule.MapCmsEndpoints</c>
/// for the group split): <see cref="MapGetPublishedPostEndpoint"/>/<see cref="MapListPublishedPostsEndpoint"/>
/// are <c>.AllowAnonymous()</c> and Published-only, mirroring
/// <c>Siri.Modules.Catalog.Features.GetCourseDetail.GetCourseDetailEndpoint</c>/
/// <c>SearchCourses.SearchCoursesEndpoint</c>'s own public/Published-only split for <c>Course</c>. The
/// remaining endpoints here are admin-only content management.
/// </para>
/// </summary>
public static class PostEndpoints
{
    /// <summary>Maps POST /api/cms/admin/posts.</summary>
    public static IEndpointRouteBuilder MapCreatePostEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/", HandleCreateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<CreatePostCommand>>()
            .WithName("CmsCreatePost")
            .WithSummary("สร้างบทความใหม่ (สถานะเริ่มต้น: ร่าง)")
            .Produces<PostResponse>(StatusCodes.Status201Created)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    /// <summary>Maps PUT /api/cms/admin/posts/{id}.</summary>
    public static IEndpointRouteBuilder MapUpdatePostEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPut("/{id:guid}", HandleUpdateAsync)
            .AddEndpointFilter<ValidationEndpointFilter<UpdatePostCommand>>()
            .WithName("CmsUpdatePost")
            .WithSummary("แก้ไขเนื้อหาบทความ")
            .Produces<PostResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>Maps POST /api/cms/admin/posts/{id}/status.</summary>
    public static IEndpointRouteBuilder MapChangePostStatusEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/{id:guid}/status", HandleChangeStatusAsync)
            .AddEndpointFilter<ValidationEndpointFilter<ChangePostStatusCommand>>()
            .WithName("CmsChangePostStatus")
            .WithSummary("เปลี่ยนสถานะบทความ (ร่าง/เผยแพร่/เก็บถาวร)")
            .Produces<PostResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
            .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        return endpoints;
    }

    /// <summary>Maps DELETE /api/cms/admin/posts/{id}.</summary>
    public static IEndpointRouteBuilder MapDeletePostEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapDelete("/{id:guid}", HandleDeleteAsync)
            .WithName("CmsDeletePost")
            .WithSummary("ลบบทความ (soft delete)")
            .Produces(StatusCodes.Status204NoContent)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>Maps GET /api/cms/admin/posts?page=&amp;pageSize=.</summary>
    public static IEndpointRouteBuilder MapListPostsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", HandleListAsync)
            .WithName("CmsListPosts")
            .WithSummary("รายการบทความทั้งหมด (ทุกสถานะ)")
            .Produces<PagedResult<PostResponse>>(StatusCodes.Status200OK);

        return endpoints;
    }

    /// <summary>Maps GET /api/cms/posts/{slug} — public, Published-only.</summary>
    public static IEndpointRouteBuilder MapGetPublishedPostEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/{slug}", HandleGetPublishedBySlugAsync)
            .AllowAnonymous()
            .WithName("CmsGetPublishedPost")
            .WithSummary("รายละเอียดบทความที่เผยแพร่แล้ว (สำหรับหน้า blog สาธารณะ)")
            .Produces<PostResponse>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    /// <summary>Maps GET /api/cms/posts?page=&amp;pageSize= — public, Published-only. The literal
    /// no-segment <c>/posts</c> route never conflicts with <see cref="MapGetPublishedPostEndpoint"/>'s
    /// <c>/posts/{slug}</c> — different pattern shapes entirely, same no-conflict reasoning
    /// <c>Siri.Modules.Catalog.Features.GetCourseDetail.GetCourseDetailEndpoint</c>'s own doc comment gives
    /// for <c>/courses/search</c> vs <c>/courses/{slug}</c>.</summary>
    public static IEndpointRouteBuilder MapListPublishedPostsEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapGet("/", HandleListPublishedAsync)
            .AllowAnonymous()
            .WithName("CmsListPublishedPosts")
            .WithSummary("รายการบทความที่เผยแพร่แล้ว (สำหรับหน้า blog สาธารณะ)")
            .Produces<PagedResult<PostResponse>>(StatusCodes.Status200OK);

        return endpoints;
    }

    private static async Task<IResult> HandleCreateAsync(
        CreatePostCommand command,
        PostService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } authorUserId)
        {
            return Results.Unauthorized();
        }

        var result = await service.CreateAsync(authorUserId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/cms/admin/posts/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleUpdateAsync(
        Guid id,
        UpdatePostCommand command,
        PostService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.UpdateAsync(id, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleChangeStatusAsync(
        Guid id,
        ChangePostStatusCommand command,
        PostService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.ChangeStatusAsync(id, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleDeleteAsync(
        Guid id,
        PostService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.DeleteAsync(id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleListAsync(
        PostService service,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20)
    {
        var result = await service.ListAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    private static async Task<IResult> HandleGetPublishedBySlugAsync(
        string slug,
        PostService service,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        var result = await service.GetPublishedBySlugAsync(slug, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> HandleListPublishedAsync(
        PostService service,
        CancellationToken cancellationToken,
        int page = 1,
        int pageSize = 20)
    {
        var result = await service.ListPublishedAsync(page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }
}
