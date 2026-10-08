namespace Siri.Modules.Catalog.Contracts;

/// <summary>
/// One of an instructor's courses with its denormalized counters (docs/contracts/P11-10-instructor-dashboard-summary.md §2.1).
/// <see cref="Status"/> / <see cref="DeliveryFormat"/> are the enum member names as strings so no Catalog Domain enum leaks across the module boundary.
/// </summary>
/// <param name="EnrollmentCount">The course's denormalized <c>EnrollmentCount</c> — the single value Catalog's stats updater maintains.</param>
/// <param name="RatingAverage">Denormalized average rating (meaningful only when <paramref name="RatingCount"/> &gt; 0).</param>
public sealed record InstructorCourseStat(
    Guid CourseId,
    string Title,
    string Slug,
    string Status,
    string DeliveryFormat,
    decimal Price,
    int EnrollmentCount,
    decimal RatingAverage,
    int RatingCount,
    string? ThumbnailUrl);

/// <summary>The <see cref="InstructorCourseStat.Status"/> value consumers compare against (the Domain enum member name; a Catalog unit test pins it).</summary>
public static class InstructorCourseStatValues
{
    public const string PublishedStatus = "Published";
}

/// <param name="InstructorProfileId"><c>CATALOG.INSTRUCTOR_PROFILES.Id</c> of the user, or <c>null</c> when the user has no instructor profile (then <paramref name="Courses"/> is empty).
/// This is the id <c>Course.InstructorId</c> and <c>REVENUE_SPLITS.INSTRUCTOR_ID</c> carry — never the user id.</param>
public sealed record InstructorCourseStatsInfo(Guid? InstructorProfileId, IReadOnlyList<InstructorCourseStat> Courses);

/// <summary>
/// Read-only per-instructor course statistics for the instructor dashboard (Analytics, P11-10). Implemented by Catalog.
/// </summary>
public interface IInstructorCourseStatsReader
{
    /// <summary>
    /// The courses owned by the instructor whose <b>user</b> id is <paramref name="instructorUserId"/> (soft-deleted courses are excluded),
    /// newest first, capped at 200. Reads the denormalized counters of the course rows — no recomputation.
    /// </summary>
    Task<InstructorCourseStatsInfo> GetByInstructorUserIdAsync(Guid instructorUserId, CancellationToken cancellationToken);
}
