using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Features.SearchCourses;

/// <summary>Bound from individual query-string parameters by <c>SearchCoursesEndpoint</c> (not a
/// client-supplied JSON body — a GET request has none), then passed to the handler as one value.</summary>
public sealed record SearchCoursesQuery(
    string? Q,
    Guid? CategoryId,
    Guid? InstructorId,
    CourseLevel? Level,
    decimal? MinPrice,
    decimal? MaxPrice,
    decimal? MinRating,
    CourseSearchSort Sort,
    int Page,
    int PageSize);
