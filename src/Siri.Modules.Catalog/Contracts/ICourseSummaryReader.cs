namespace Siri.Modules.Catalog.Contracts;

public sealed record CourseSummaryInfo(
    Guid CourseId,
    string Slug,
    string Title,
    string? ThumbnailUrl,
    string? InstructorName);

/// <summary>Course metadata for existing enrollments, including courses no longer published.</summary>
public interface ICourseSummaryReader
{
    Task<IReadOnlyDictionary<Guid, CourseSummaryInfo>> GetCourseSummariesAsync(
        IEnumerable<Guid> courseIds,
        CancellationToken cancellationToken);
}
