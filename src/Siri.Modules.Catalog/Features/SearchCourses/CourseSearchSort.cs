namespace Siri.Modules.Catalog.Features.SearchCourses;

/// <summary>Sort order for <see cref="SearchCoursesHandler"/>'s results — a query-time preference, not
/// persisted state (unlike <c>CourseStatus</c>/<c>CourseLevel</c>/<c>CourseLanguage</c> in
/// <c>Domain</c>), so it lives in this feature folder instead.</summary>
public enum CourseSearchSort
{
    /// <summary>Default. Meaningless without a search term — <see cref="SearchCoursesHandler"/> falls
    /// back to <see cref="Newest"/> when no search text was given.</summary>
    Relevance,
    Newest,
    PriceAsc,
    PriceDesc,
    RatingDesc,
}
