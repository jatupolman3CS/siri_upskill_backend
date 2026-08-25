namespace Siri.Modules.Catalog.Domain;

/// <summary>Not specified by any doc — matches docs/DECISIONS.md's "TH หลัก + รองรับ EN" strategy.
/// String-backed (see <c>CourseConfiguration</c>), so extending this list later needs no migration.</summary>
public enum CourseLanguage
{
    Thai,
    English,
}
