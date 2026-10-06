using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.Modules.Catalog.Features.AttachEpisodeMedia;
using Siri.Modules.Catalog.Features.AutosaveCourse;
using Siri.Modules.Catalog.Features.CreateCourse;
using Siri.Modules.Catalog.Features.CreateCourseEpisode;
using Siri.Modules.Catalog.Features.CreateCourseSection;
using Siri.Modules.Catalog.Features.DeleteCourse;
using Siri.Modules.Catalog.Features.DeleteCourseEpisode;
using Siri.Modules.Catalog.Features.DeleteCourseSection;
using Siri.Modules.Catalog.Features.GetCourse;
using Siri.Modules.Catalog.Features.GetCourseBuilder;
using Siri.Modules.Catalog.Features.GetMyCourses;
using Siri.Modules.Catalog.Features.ReorderCourseEpisodes;
using Siri.Modules.Catalog.Features.ReorderCourseSections;
using Siri.Modules.Catalog.Features.SetCourseDeliveryFormat;
using Siri.Modules.Catalog.Features.SetCourseEnrollmentPolicy;
using Siri.Modules.Catalog.Features.SubmitCourseForReview;
using Siri.Modules.Catalog.Features.UpdateCourse;
using Siri.Modules.Catalog.Features.UpdateCourseEpisode;
using Siri.Modules.Catalog.Features.UpdateCourseSection;
using Siri.SharedKernel;

namespace Siri.Api.Controllers.Catalog;

[ApiController]
[Route("api/catalog/instructor/courses")]
[Authorize(Policy = AuthorizationPolicyNames.InstructorOnly)]
[Tags("Catalog")]
public class InstructorCoursesController : ControllerBase
{
    [HttpPost("")]
    [EndpointName("CatalogCreateCourse")]
    [EndpointSummary("สร้างคอร์สฉบับร่างใหม่")]
    [ProducesResponseType(typeof(CreateCourseResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> CreateCourse(
        [FromBody] CreateCourseCommand command,
        [FromServices] CreateCourseHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/catalog/instructor/courses/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("")]
    [EndpointName("CatalogGetMyCourses")]
    [EndpointSummary("รายการคอร์สของตัวเอง")]
    [ProducesResponseType(typeof(PagedResult<CourseSummary>), StatusCodes.Status200OK)]
    public async Task<IResult> GetMyCourses(
        [FromServices] GetMyCoursesHandler handler,
        [FromServices] IUserContext userContext,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = GetMyCoursesHandler.DefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, page, pageSize, cancellationToken).ConfigureAwait(false);
        return Results.Ok(result);
    }

    [HttpGet("{id:guid}")]
    [EndpointName("CatalogGetCourse")]
    [EndpointSummary("ดูรายละเอียดคอร์สของตัวเอง")]
    [ProducesResponseType(typeof(CourseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetCourse(
        [FromRoute] Guid id,
        [FromServices] GetCourseHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPut("{id:guid}")]
    [EndpointName("CatalogUpdateCourse")]
    [EndpointSummary("แก้ไขคอร์สฉบับร่าง")]
    [ProducesResponseType(typeof(UpdateCourseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> UpdateCourse(
        [FromRoute] Guid id,
        [FromBody] UpdateCourseCommand command,
        [FromServices] UpdateCourseHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, id, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpDelete("{id:guid}")]
    [EndpointName("CatalogDeleteCourse")]
    [EndpointSummary("ลบคอร์สฉบับร่าง")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> DeleteCourse(
        [FromRoute] Guid id,
        [FromServices] DeleteCourseHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.NoContent()
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("{id:guid}/submit-for-review")]
    [EndpointName("CatalogSubmitCourseForReview")]
    [EndpointSummary("ส่งคอร์สให้แอดมินตรวจสอบเพื่อเผยแพร่")]
    [ProducesResponseType(typeof(SubmitCourseForReviewResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> SubmitCourseForReview(
        [FromRoute] Guid id,
        [FromServices] SubmitCourseForReviewHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpGet("{id:guid}/builder")]
    [EndpointName("CatalogGetCourseBuilder")]
    [EndpointSummary("ดึงข้อมูลโครงสร้างคอร์สทั้งหมดสำหรับ Course Builder")]
    [ProducesResponseType(typeof(CourseBuilderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<IResult> GetCourseBuilder(
        [FromRoute] Guid id,
        [FromServices] GetCourseBuilderHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, id, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("{courseId:guid}/sections")]
    [EndpointName("CatalogCreateCourseSection")]
    [EndpointSummary("สร้างส่วน/บทหลักใหม่ในคอร์ส")]
    [ProducesResponseType(typeof(CourseSectionResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> CreateCourseSection(
        [FromRoute] Guid courseId,
        [FromBody] CreateCourseSectionCommand command,
        [FromServices] CreateCourseSectionHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, courseId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/catalog/instructor/courses/{courseId}/sections/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPut("{courseId:guid}/sections/{sectionId:guid}")]
    [EndpointName("CatalogUpdateCourseSection")]
    [EndpointSummary("แก้ไขส่วน/บทหลักในคอร์ส")]
    [ProducesResponseType(typeof(CourseSectionResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> UpdateCourseSection(
        [FromRoute] Guid courseId,
        [FromRoute] Guid sectionId,
        [FromBody] UpdateCourseSectionCommand command,
        [FromServices] UpdateCourseSectionHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, courseId, sectionId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpDelete("{courseId:guid}/sections/{sectionId:guid}")]
    [EndpointName("CatalogDeleteCourseSection")]
    [EndpointSummary("ลบส่วน/บทหลักในคอร์ส")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> DeleteCourseSection(
        [FromRoute] Guid courseId,
        [FromRoute] Guid sectionId,
        [FromServices] DeleteCourseSectionHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, courseId, sectionId, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.NoContent()
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPut("{courseId:guid}/sections/reorder")]
    [EndpointName("CatalogReorderCourseSections")]
    [EndpointSummary("จัดลำดับส่วน/บทหลักทั้งหมดในคอร์ส")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> ReorderCourseSections(
        [FromRoute] Guid courseId,
        [FromBody] ReorderCourseSectionsCommand command,
        [FromServices] ReorderCourseSectionsHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, courseId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok()
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPost("{courseId:guid}/sections/{sectionId:guid}/episodes")]
    [EndpointName("CatalogCreateCourseEpisode")]
    [EndpointSummary("สร้างบทเรียนใหม่ในส่วน/บทหลัก")]
    [ProducesResponseType(typeof(CourseEpisodeResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> CreateCourseEpisode(
        [FromRoute] Guid courseId,
        [FromRoute] Guid sectionId,
        [FromBody] CreateCourseEpisodeCommand command,
        [FromServices] CreateCourseEpisodeHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, courseId, sectionId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Created($"/api/catalog/instructor/courses/{courseId}/sections/{sectionId}/episodes/{result.Value.Id}", result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPut("{courseId:guid}/sections/{sectionId:guid}/episodes/{episodeId:guid}")]
    [EndpointName("CatalogUpdateCourseEpisode")]
    [EndpointSummary("แก้ไขบทเรียนในส่วน/บทหลัก")]
    [ProducesResponseType(typeof(CourseEpisodeResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> UpdateCourseEpisode(
        [FromRoute] Guid courseId,
        [FromRoute] Guid sectionId,
        [FromRoute] Guid episodeId,
        [FromBody] UpdateCourseEpisodeCommand command,
        [FromServices] UpdateCourseEpisodeHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, courseId, sectionId, episodeId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpDelete("{courseId:guid}/sections/{sectionId:guid}/episodes/{episodeId:guid}")]
    [EndpointName("CatalogDeleteCourseEpisode")]
    [EndpointSummary("ลบบทเรียนในส่วน/บทหลัก")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> DeleteCourseEpisode(
        [FromRoute] Guid courseId,
        [FromRoute] Guid sectionId,
        [FromRoute] Guid episodeId,
        [FromServices] DeleteCourseEpisodeHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, courseId, sectionId, episodeId, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.NoContent()
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPut("{courseId:guid}/sections/{sectionId:guid}/episodes/reorder")]
    [EndpointName("CatalogReorderCourseEpisodes")]
    [EndpointSummary("จัดลำดับบทเรียนทั้งหมดในส่วน/บทหลัก")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> ReorderCourseEpisodes(
        [FromRoute] Guid courseId,
        [FromRoute] Guid sectionId,
        [FromBody] ReorderCourseEpisodesCommand command,
        [FromServices] ReorderCourseEpisodesHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, courseId, sectionId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok()
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPut("{courseId:guid}/autosave")]
    [EndpointName("CatalogAutosaveCourse")]
    [EndpointSummary("บันทึกอัตโนมัติ/อัปเดตข้อมูลโครงสร้างคอร์สทั้งหมด (Course Builder)")]
    [ProducesResponseType(typeof(AutosaveCourseResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> AutosaveCourse(
        [FromRoute] Guid courseId,
        [FromBody] AutosaveCourseCommand command,
        [FromServices] AutosaveCourseHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, courseId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPut("{courseId:guid}/sections/{sectionId:guid}/episodes/{episodeId:guid}/media")]
    [EndpointName("CatalogAttachEpisodeMedia")]
    [EndpointSummary("ผูกวิดีโอ (Media Asset) เข้ากับบทเรียน")]
    [ProducesResponseType(typeof(EpisodeMediaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> AttachEpisodeMedia(
        [FromRoute] Guid courseId,
        [FromRoute] Guid sectionId,
        [FromRoute] Guid episodeId,
        [FromBody] AttachEpisodeMediaCommand command,
        [FromServices] AttachEpisodeMediaHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.AttachAsync(userId, courseId, sectionId, episodeId, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpDelete("{courseId:guid}/sections/{sectionId:guid}/episodes/{episodeId:guid}/media")]
    [EndpointName("CatalogRemoveEpisodeMedia")]
    [EndpointSummary("ถอดวิดีโอ (Media Asset) ออกจากบทเรียน")]
    [ProducesResponseType(typeof(EpisodeMediaResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> RemoveEpisodeMedia(
        [FromRoute] Guid courseId,
        [FromRoute] Guid sectionId,
        [FromRoute] Guid episodeId,
        [FromServices] AttachEpisodeMediaHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.RemoveAsync(userId, courseId, sectionId, episodeId, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPut("{id:guid}/delivery-format")]
    [EndpointName("CatalogSetCourseDeliveryFormat")]
    [EndpointSummary("เปลี่ยนรูปแบบการส่งมอบคอร์ส (OnDemand, Live, Hybrid)")]
    [ProducesResponseType(typeof(SetCourseDeliveryFormatResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> SetDeliveryFormat(
        [FromRoute] Guid id,
        [FromBody] SetCourseDeliveryFormatCommand command,
        [FromServices] SetCourseDeliveryFormatHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, id, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }

    [HttpPut("{id:guid}/enrollment-policy")]
    [EndpointName("CatalogSetCourseEnrollmentPolicy")]
    [EndpointSummary("ตั้งนโยบายปิดรับสมัคร/เพดานที่นั่งของคอร์ส")]
    [ProducesResponseType(typeof(SetCourseEnrollmentPolicyResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ValidationProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<IResult> SetEnrollmentPolicy(
        [FromRoute] Guid id,
        [FromBody] SetCourseEnrollmentPolicyCommand command,
        [FromServices] SetCourseEnrollmentPolicyHandler handler,
        [FromServices] IUserContext userContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await handler.HandleAsync(userId, id, command, cancellationToken).ConfigureAwait(false);

        return result.IsSuccess
            ? Results.Ok(result.Value)
            : result.Error.ToProblemHttpResult(HttpContext);
    }
}
