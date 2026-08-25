namespace Siri.Modules.Catalog.Domain;

/// <summary>Not specified by any doc — a reasonable, low-confidence default for a course-search/filter
/// facet (ARCHITECTURE.md lists Level as an index/filter column). String-backed (see
/// <c>CourseConfiguration</c>), so extending this list later needs no migration.</summary>
public enum CourseLevel
{
    Beginner,
    Intermediate,
    Advanced,
    AllLevels,
}
