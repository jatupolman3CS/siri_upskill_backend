using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Catalog.Features.CreateLearningPath;
using Siri.Modules.Catalog.Features.DeleteLearningPath;
using Siri.Modules.Catalog.Features.GetLearningPathBySlug;
using Siri.Modules.Catalog.Features.GetLearningPaths;
using Siri.Modules.Catalog.Features.UpdateLearningPath;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features;

public static class LearningPathEndpoints
{
    public static IEndpointRouteBuilder MapLearningPathEndpoints(this IEndpointRouteBuilder endpoints)
    {
        // Public endpoints
        var publicGroup = endpoints.MapGroup("/learning-paths");

        publicGroup.MapGet("/", async (
            GetLearningPathsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paths = await handler.HandleAsync(activeOnly: true, cancellationToken).ConfigureAwait(false);
            return Results.Ok(paths);
        })
        .AllowAnonymous()
        .WithName("GetPublicLearningPaths")
        .WithSummary("ดึงรายการเส้นทางการเรียนที่เปิดใช้งาน")
        .Produces<IReadOnlyList<LearningPathSummaryResponse>>(StatusCodes.Status200OK);

        publicGroup.MapGet("/{slug}", async (
            string slug,
            GetLearningPathBySlugHandler handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(slug, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
        })
        .AllowAnonymous()
        .WithName("GetLearningPathBySlug")
        .WithSummary("ดึงรายละเอียดเส้นทางการเรียนตาม slug")
        .Produces<LearningPathDetailResponse>(StatusCodes.Status200OK)
        .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        // Admin endpoints
        var adminGroup = endpoints.MapGroup("/admin/learning-paths")
            .RequireAuthorization(AuthorizationPolicyNames.AdminOnly);

        adminGroup.MapGet("/", async (
            GetLearningPathsHandler handler,
            CancellationToken cancellationToken) =>
        {
            var paths = await handler.HandleAsync(activeOnly: false, cancellationToken).ConfigureAwait(false);
            return Results.Ok(paths);
        })
        .WithName("GetAdminLearningPaths")
        .WithSummary("ดึงรายการเส้นทางการเรียนทั้งหมด (แอดมิน)")
        .Produces<IReadOnlyList<LearningPathSummaryResponse>>(StatusCodes.Status200OK);

        adminGroup.MapPost("/", async (
            CreateLearningPathCommand command,
            CreateLearningPathHandler handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(command, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess
                ? Results.Created($"/api/catalog/learning-paths/{result.Value.Slug}", result.Value)
                : result.Error.ToProblemHttpResult(httpContext);
        })
        .AddEndpointFilter<ValidationEndpointFilter<CreateLearningPathCommand>>()
        .WithName("CreateLearningPath")
        .WithSummary("สร้างเส้นทางการเรียนใหม่")
        .Produces<LearningPathDetailResponse>(StatusCodes.Status201Created)
        .ProducesValidationProblem()
        .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        adminGroup.MapPut("/{id:guid}", async (
            Guid id,
            UpdateLearningPathCommand command,
            UpdateLearningPathHandler handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, command, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
        })
        .AddEndpointFilter<ValidationEndpointFilter<UpdateLearningPathCommand>>()
        .WithName("UpdateLearningPath")
        .WithSummary("แก้ไขเส้นทางการเรียน")
        .Produces<LearningPathDetailResponse>(StatusCodes.Status200OK)
        .ProducesValidationProblem()
        .Produces<ProblemDetails>(StatusCodes.Status404NotFound)
        .Produces<ProblemDetails>(StatusCodes.Status409Conflict);

        adminGroup.MapDelete("/{id:guid}", async (
            Guid id,
            DeleteLearningPathHandler handler,
            HttpContext httpContext,
            CancellationToken cancellationToken) =>
        {
            var result = await handler.HandleAsync(id, cancellationToken).ConfigureAwait(false);
            return result.IsSuccess ? Results.NoContent() : result.Error.ToProblemHttpResult(httpContext);
        })
        .WithName("DeleteLearningPath")
        .WithSummary("ลบเส้นทางการเรียน")
        .Produces(StatusCodes.Status204NoContent)
        .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }
}
