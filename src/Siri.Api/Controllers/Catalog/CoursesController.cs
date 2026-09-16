using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.OutputCaching;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Catalog.Domain;
using Siri.Modules.Catalog.Features.CreateCourseReview;
using Siri.Modules.Catalog.Features.GetCourseDetail;
using Siri.Modules.Catalog.Features.GetCourseReviews;
using Siri.Modules.Catalog.Features.SearchCourses;
using Siri.Modules.Catalog.Infrastructure;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Catalog;

[ApiController]
[Route("api/catalog/courses")]
[Tags("Catalog")]
public class CoursesController : ControllerBase
{
    [HttpGet("search")]
    [AllowAnonymous]
    [OutputCache(PolicyName = CourseOutputCache.PolicyName)]
    [EndpointName("CatalogSearchCourses")]
    [EndpointSummary("ค้นหาและกรองคอร์สที่เผยแพร่แล้ว")]
    [ProducesResponseType(typeof(SearchCoursesResponse), StatusCodes.Status200OK)]
    public async Task<IResult> SearchCourses(
        [FromServices] SearchCoursesHandler handler,
        [FromQuery] string? q = null,
        [FromQuery] Guid? categoryId = null,
        [FromQuery] Guid? instructorId = null,
        [FromQuery] CourseLevel? level = null,
        [FromQuery] decimal? minPrice = null,
        [FromQuery] decimal? maxPrice = null,
        [FromQuery] decimal? minRating = null,
        [FromQuery] CourseSearchSort sort = CourseSearchSort.Relevance,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = SearchCoursesHandler.DefaultPageSize,
        [FromQuery] DeliveryFormat? format = null,
        CancellationToken cancellationToken = default)
    {
        var query = new SearchCoursesQuery(q, categoryId, instructorId, level, minPrice, maxPrice, minRating, sort, page, pageSize, format);
        var result = await handler.HandleAsync(query, cancellationToken).ConfigureAwait(false);

        return Results.Ok(result);
    }

    [HttpGet("{slug}")]
    [AllowAnonymous]
    [OutputCache(PolicyName = CourseOutputCache.PolicyName)]
    [EndpointName("CatalogGetCourseDetail")]
    [EndpointSummary("รายละเอียดคอร์สที่เผยแพร่แล้ว (สำหรับหน้า course detail สาธารณะ)")]
    [ProducesResponseType(typeof(CourseDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetCourseDetail(
        [FromRoute] string slug,
        [FromServices] GetCourseDetailHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(slug, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("{courseId:guid}/reviews")]
    [AllowAnonymous]
    [EndpointName("GetCourseReviews")]
    [EndpointSummary("ดึงรายการรีวิวและความคิดเห็นของคอร์สเรียน")]
    [ProducesResponseType(typeof(CourseReviewSummaryResponse), StatusCodes.Status200OK)]
    public async Task<IResult> GetCourseReviews(
        [FromRoute] Guid courseId,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        [FromServices] GetCourseReviewsHandler handler,
        CancellationToken cancellationToken)
    {
        var result = await handler.HandleAsync(courseId, page ?? 1, pageSize ?? 10, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("{courseId:guid}/reviews")]
    [Authorize]
    [EndpointName("CreateCourseReview")]
    [EndpointSummary("เขียนรีวิวและให้คะแนนคอร์สเรียน (เฉพาะผู้ที่ลงทะเบียนเรียน)")]
    [ProducesResponseType(typeof(CourseReviewDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<IResult> CreateCourseReview(
        [FromRoute] Guid courseId,
        [FromBody] CreateCourseReviewRequest request,
        [FromServices] CreateCourseReviewHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (!userContext.UserId.HasValue) return Results.Unauthorized();
        var result = await handler.HandleAsync(courseId, userContext.UserId.Value, request, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(HttpContext);
    }
}
