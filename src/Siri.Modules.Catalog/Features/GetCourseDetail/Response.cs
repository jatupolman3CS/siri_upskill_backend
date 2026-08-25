using Siri.Modules.Catalog.Domain;

namespace Siri.Modules.Catalog.Features.GetCourseDetail;

public sealed record CourseDetailResponse(
    Guid Id,
    string Slug,
    string Title,
    string? Subtitle,
    string? Description,
    CourseLevel Level,
    CourseLanguage Language,
    string? ThumbnailUrl,
    decimal Price,
    decimal? ComparePrice,
    string Currency,
    int? AccessDurationDays,
    decimal RatingAverage,
    int RatingCount,
    int EnrollmentCount,
    int EpisodeCount,
    int TotalDurationSeconds,
    string? SeoTitle,
    string? SeoDescription,
    DateTime? PublishedAtUtc,
    Guid CategoryId,
    CourseDetailInstructor Instructor,
    IReadOnlyList<string> Outcomes,
    IReadOnlyList<string> Requirements,
    IReadOnlyList<CourseDetailSection> Sections);

public sealed record CourseDetailInstructor(Guid Id, string DisplayName, string? Headline, string? AvatarUrl);

/// <summary>Syllabus structure only — no <c>MediaAssetId</c> or anything playback-related. Actual video
/// access is gated by Phase 2's playback-session API (enrollment + entitlement checks); this response
/// exists to render the public "syllabus accordion" (docs/REQUIREMENTS.md LX-02), not to grant access to
/// anything.</summary>
public sealed record CourseDetailSection(Guid Id, string Title, int SortOrder, IReadOnlyList<CourseDetailEpisode> Episodes);

public sealed record CourseDetailEpisode(Guid Id, string Title, int SortOrder, int? DurationSeconds, bool IsFreePreview);
