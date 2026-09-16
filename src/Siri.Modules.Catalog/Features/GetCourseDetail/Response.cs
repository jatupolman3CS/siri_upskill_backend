using Siri.Modules.Catalog.Contracts;
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
    IReadOnlyList<CourseDetailSection> Sections,
    bool IsWishlisted,
    DeliveryFormat DeliveryFormat,
    CourseDetailLiveSchedule? LiveSchedule = null);

public sealed record CourseDetailInstructor(Guid Id, string DisplayName, string? Headline, string? AvatarUrl);

/// <summary>Syllabus structure only — no <c>MediaAssetId</c> or anything playback-related. Actual video
/// access is gated by Phase 2's playback-session API (enrollment + entitlement checks); this response
/// exists to render the public "syllabus accordion" (docs/REQUIREMENTS.md LX-02), not to grant access to
/// anything.</summary>
public sealed record CourseDetailSection(Guid Id, string Title, int SortOrder, IReadOnlyList<CourseDetailEpisode> Episodes);

public sealed record CourseDetailEpisode(Guid Id, string Title, int SortOrder, int? DurationSeconds, bool IsFreePreview);

/// <summary>null เมื่อ DeliveryFormat == OnDemand เท่านั้น (ไม่มีอะไรให้แสดง) — Live/Hybrid ได้ค่านี้เสมอแม้
/// Sessions จะว่างเปล่า (ยังไม่ตั้งตารางสอน — ไม่ error, แค่ list ว่าง).</summary>
public sealed record CourseDetailLiveSchedule(
    string Timezone,
    int UpcomingCount,
    int PastCount,
    DateTime? NextStartsAtUtc,
    IReadOnlyList<CourseDetailLiveSession> Sessions);

/// <summary>**ไม่มี meetUrl โดยตั้งใจ — เป็น security requirement ไม่ใช่แค่ scope** (docs/HYBRID_LIVE.md §2.1:
/// "Meet URL ไม่เคยอยู่ใน response สาธารณะ") ต้องมี integration test ยืนยันด้วย JSON path ตรง ๆ ว่าไม่มี
/// property ชื่อ meetUrl/meetingUrl หลุดออกมาเลย (ดู §5 integration checklist).</summary>
public sealed record CourseDetailLiveSession(
    Guid Id,
    string Title,
    DateTime StartsAtUtc,
    DateTime EndsAtUtc,
    LiveSessionDisplayState DisplayState,
    bool HasRecording);
