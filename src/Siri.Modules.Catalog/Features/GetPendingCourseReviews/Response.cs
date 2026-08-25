namespace Siri.Modules.Catalog.Features.GetPendingCourseReviews;

public sealed record PendingCourseReviewSummary(
    Guid Id,
    string Slug,
    string Title,
    Guid InstructorId,
    string? InstructorName,
    Guid CategoryId,
    string? CategoryName,
    decimal Price,
    string? ThumbnailUrl,
    int EpisodeCount,
    int TotalDurationMinutes,
    DateTime? SubmittedAtUtc);
