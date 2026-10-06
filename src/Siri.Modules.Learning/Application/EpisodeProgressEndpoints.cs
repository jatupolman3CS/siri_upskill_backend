using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Siri.SharedKernel;

namespace Siri.Modules.Learning.Application;

/// <summary>
/// Maps every <see cref="Domain.EPISODE_PROGRESS"/> HTTP endpoint. Self-contained top-level
/// <c>/api/learning</c> <c>MapGroup</c> — see <c>EnrollmentEndpoints</c>'s own doc comment for why.
/// <para>
/// Nested under <c>/enrollments/{enrollmentId}/episode-progress</c> rather than a flat
/// <c>/episode-progress</c> collection — <see cref="Domain.EPISODE_PROGRESS"/> has no <c>UserId</c> of its
/// own, only an <c>EnrollmentId</c> (see <see cref="EpisodeProgressService"/>'s own doc comment), so tying
/// the route to <c>enrollmentId</c> directly is what lets every handler here ask "does
/// <paramref name="enrollmentId"/>-as-in-the-URL actually belong to the caller" as its ownership check,
/// the same id the request is already scoped by rather than a separate implicit lookup.
/// </para>
/// <para>
/// Bare-authenticated only — no admin surface for this cluster in this scaffold pass (unlike
/// <c>EnrollmentEndpoints</c>/<c>CertificateEndpoints</c>): progress rows are learner-private telemetry
/// with no ops/backfill use case identified yet, so nothing beyond "read/write my own" is scaffolded here.
/// </para>
/// <para>
/// Endpoint bodies here are real plumbing (bind → call <see cref="EpisodeProgressService"/> → map
/// <see cref="Result"/> to HTTP), not part of the "business logic left as stubs" scope.
/// </para>
/// </summary>
public static class EpisodeProgressEndpoints
{
    public static IEndpointRouteBuilder MapEpisodeProgressEndpoints(this IEndpointRouteBuilder endpoints)
    {
        var group = endpoints.MapGroup("/api/learning/enrollments/{enrollmentId:guid}/episode-progress")
            .WithTags("Learning")
            .RequireAuthorization();

        group.MapPut("/{episodeId:guid}", UpsertAsync)
            .RequireRateLimiting("heartbeat")
            .AddEndpointFilter<ValidationEndpointFilter<UpsertEpisodeProgressCommand>>()
            .WithName("LearningUpsertEpisodeProgress")
            .WithSummary("บันทึกความคืบหน้าการดูวิดีโอของบทเรียน (heartbeat)")
            .Produces<EpisodeProgressResponse>(StatusCodes.Status200OK)
            .ProducesValidationProblem()
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        group.MapGet("/", ListForEnrollmentAsync)
            .WithName("LearningListEpisodeProgressForEnrollment")
            .WithSummary("ดูความคืบหน้าทุกบทเรียนของการลงทะเบียนหนึ่งรายการ")
            .Produces<IReadOnlyList<EpisodeProgressResponse>>(StatusCodes.Status200OK)
            .Produces<ProblemDetails>(StatusCodes.Status401Unauthorized)
            .Produces<ProblemDetails>(StatusCodes.Status404NotFound);

        return endpoints;
    }

    private static async Task<IResult> UpsertAsync(
        Guid enrollmentId,
        Guid episodeId,
        UpsertEpisodeProgressCommand command,
        EpisodeProgressService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.UpsertProgressAsync(userId, enrollmentId, episodeId, command, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }

    private static async Task<IResult> ListForEnrollmentAsync(
        Guid enrollmentId,
        EpisodeProgressService service,
        IUserContext userContext,
        HttpContext httpContext,
        CancellationToken cancellationToken)
    {
        if (userContext.UserId is not { } userId)
        {
            return Results.Unauthorized();
        }

        var result = await service.GetForEnrollmentAsync(userId, enrollmentId, cancellationToken).ConfigureAwait(false);
        return result.IsSuccess ? Results.Ok(result.Value) : result.Error.ToProblemHttpResult(httpContext);
    }
}
