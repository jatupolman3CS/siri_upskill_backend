using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Catalog.Application;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Catalog;

/// <summary>
/// Operator tools for the Meilisearch course index: see whether it is on, reachable and complete, and force a full reindex (e.g. right after
/// pointing the API at a new Meilisearch, or after seeding). AdminOnly. Neither action returns the Meilisearch URL or key.
/// </summary>
[ApiController]
[Route("api/catalog/admin/search")]
[Authorize(Policy = AuthorizationPolicyNames.AdminOnly)]
[Tags("Catalog")]
public class CourseSearchAdminController : ControllerBase
{
    private static readonly DomainError DisabledError = DomainError.Conflict(
        "Meilisearch ไม่ได้เปิดใช้งาน (ยังไม่ได้ตั้งค่า Meilisearch:Url / Meilisearch:ApiKey) ระบบค้นหาใช้ PostgreSQL อยู่");

    [HttpGet("status")]
    [EndpointName("CatalogGetCourseSearchStatus")]
    [EndpointSummary("สถานะ Meilisearch ของการค้นหาคอร์ส (เปิดใช้งาน/เชื่อมต่อได้/จำนวนเอกสารเทียบกับฐานข้อมูล)")]
    [ProducesResponseType(typeof(CourseSearchStatus), StatusCodes.Status200OK)]
    public async Task<IResult> GetStatus(
        [FromServices] CourseSearchIndexer indexer,
        CancellationToken cancellationToken)
    {
        var status = await indexer.GetStatusAsync(cancellationToken).ConfigureAwait(false);
        return Results.Ok(status);
    }

    [HttpPost("reindex")]
    [EndpointName("CatalogReindexCourseSearch")]
    [EndpointSummary("สร้างดัชนีค้นหาคอร์สใหม่ทั้งหมดจากฐานข้อมูล")]
    [ProducesResponseType(typeof(CourseSearchReindexResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status503ServiceUnavailable)]
    public async Task<IResult> Reindex(
        [FromServices] CourseSearchIndexer indexer,
        CancellationToken cancellationToken)
    {
        if (!indexer.IsEnabled)
        {
            return DisabledError.ToProblemHttpResult(HttpContext);
        }

        try
        {
            var result = await indexer.ReindexAllAsync(cancellationToken).ConfigureAwait(false);
            return Results.Ok(result);
        }
        catch (CourseSearchIndexException ex)
        {
            // The exception text comes from Meilisearch's own error body (never the key); it tells the operator what to fix.
            return DomainError.Unavailable($"ติดต่อ Meilisearch ไม่สำเร็จ: {ex.Message}").ToProblemHttpResult(HttpContext);
        }
    }
}
