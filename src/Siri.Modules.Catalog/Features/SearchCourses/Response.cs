using Siri.Modules.Catalog.Domain;
using Siri.SharedKernel;

namespace Siri.Modules.Catalog.Features.SearchCourses;

/// <summary>
/// <see cref="Facets"/> are computed against the search-text-matched, Published set BEFORE this
/// request's own category/instructor/level/price/rating filters are applied — a deliberate v1 simplification, not
/// "current filters minus this facet's own dimension" (the more familiar e-commerce-style faceted
/// search UX, where switching category still respects the other active filters). The fuller version
/// needs one extra query per facet dimension, each re-applying every filter except its own; this
/// codebase's catalog is small enough for v1 (docs/DECISIONS.md D-10: "พอสำหรับ catalog &lt; ~10k คอร์ส")
/// that the simpler version is a reasonable place to start, and upgrading later doesn't change this
/// response's shape.
/// </summary>
public sealed record SearchCoursesResponse(PagedResult<CourseSearchResultItem> Results, CourseSearchFacets Facets);

public sealed record CourseSearchResultItem(
    Guid Id,
    string Slug,
    string Title,
    string? Subtitle,
    string? ThumbnailUrl,
    decimal Price,
    decimal? ComparePrice,
    string Currency,
    CourseLevel Level,
    CourseLanguage Language,
    decimal RatingAverage,
    int RatingCount,
    int EnrollmentCount,
    int EpisodeCount,
    int TotalDurationSeconds,
    Guid InstructorId,
    string InstructorDisplayName,
    Guid CategoryId);

public sealed record CourseSearchFacets(
    IReadOnlyList<CategoryFacet> Categories,
    IReadOnlyList<LevelFacet> Levels,
    IReadOnlyList<InstructorFacet> Instructors);

public sealed record CategoryFacet(Guid CategoryId, int Count);

public sealed record LevelFacet(CourseLevel Level, int Count);

/// <summary>Carries <see cref="DisplayName"/> unlike the other facets — the frontend can resolve a
/// category id against the category tree it already fetches, but has no instructor directory to resolve
/// an instructor id against, so the name must ride along or the facet is unrenderable (P1-10).</summary>
public sealed record InstructorFacet(Guid InstructorId, string DisplayName, int Count);
