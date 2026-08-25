using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Features.GetMyCourses;

/// <summary>Lean projection for the "my courses" list — full detail is <c>GetCourse</c>'s job for a
/// single course (database.md: "Projection ไปเป็น DTO ตรง ๆ ดีกว่าดึง entity มาทั้งก้อนแล้ว map").</summary>
public sealed record CourseSummary(
    Guid Id,
    string Slug,
    string Title,
    string? ThumbnailUrl,
    decimal Price,
    CourseStatus Status,
    DateTime CreatedAtUtc);
