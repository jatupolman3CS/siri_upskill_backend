namespace Siri.Modules.Catalog.Features.Wishlist;

public sealed record WishlistCourseItemResponse(
    Guid CourseId,
    string Slug,
    string Title,
    string? Subtitle,
    string? ThumbnailUrl,
    decimal Price,
    decimal? ComparePrice,
    string Currency,
    string InstructorName,
    decimal RatingAverage,
    int RatingCount,
    DateTime WishlistedAtUtc);
